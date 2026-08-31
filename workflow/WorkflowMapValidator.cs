using System.Text.Json;
using Json.Schema;
using ModuleDev.Contracts;

namespace ModuleDev.Workflow;

public sealed class WorkflowMapValidator
{
    private const string ExpectedContractVersion = "course-1";
    private const string RequiredWorkerPrincipal = "workflow-worker";

    // Scopes из нормативного справочника недели 1.
    // Для hidden actions validator проверяет, что их required_policy
    // является подмножеством этих доверенных server-side scopes.
    private static readonly IReadOnlySet<string> WorkerScopes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "workflow:execute",
            "payment:internal"
        };

    private readonly JsonSchema _schema;
    private readonly EvaluationOptions _schemaOptions = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true
    };

    private readonly IActionCatalog _actions;

    public WorkflowMapValidator(
        string schemaJson,
        IActionCatalog actions)
    {
        _schema = JsonSchema.FromText(schemaJson);
        _actions = actions;
    }

    public async Task<WorkflowMapValidationResult> ValidateAsync(
        string mapJson,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(mapJson);
        }
        catch (JsonException ex)
        {
            return WorkflowMapValidationResult.Failure(
                $"Invalid JSON: {ex.Message}");
        }

        using (document)
        {
            var schemaResult = _schema.Evaluate(
                document.RootElement,
                _schemaOptions);

            if (!schemaResult.IsValid)
            {
                return WorkflowMapValidationResult.Failure(
                    "Workflow map does not match the course-1 workflow map schema.");
            }

            WorkflowMap map;

            try
            {
                map = WorkflowMapLoader.Load(mapJson);
            }
            catch (JsonException ex)
            {
                return WorkflowMapValidationResult.Failure(
                    $"Workflow map could not be deserialized: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                return WorkflowMapValidationResult.Failure(
                    ex.Message);
            }

            ValidateTopLevel(map, errors);
            ValidateSteps(map, errors);
            ValidateTransitions(map, errors);
            ValidateGraph(map, errors);
            ValidateMappings(map, errors);
            ValidateRetryPolicies(map, errors);

            await ValidateActionsAndPoliciesAsync(
                map,
                errors,
                cancellationToken);

            return errors.Count == 0
                ? WorkflowMapValidationResult.Success()
                : WorkflowMapValidationResult.Failure(errors);
        }
    }

    private static void ValidateTopLevel(
        WorkflowMap map,
        List<string> errors)
    {
        if (!string.Equals(
                map.ContractVersion,
                ExpectedContractVersion,
                StringComparison.Ordinal))
        {
            errors.Add(
                $"contract_version must be '{ExpectedContractVersion}'.");
        }

        if (string.IsNullOrWhiteSpace(map.FlowName))
        {
            errors.Add("flow_name must not be empty.");
        }

        if (map.Version < 1)
        {
            errors.Add("version must be >= 1.");
        }

        if (map.Steps.Count < 2)
        {
            errors.Add("workflow map must contain at least two steps.");
        }

        if (map.Transitions.Count == 0)
        {
            errors.Add("workflow map must contain at least one transition.");
        }

        if (map.Steps.Count(
                step => string.Equals(
                    step.Key,
                    map.StartStep,
                    StringComparison.Ordinal)) != 1)
        {
            errors.Add(
                $"start_step '{map.StartStep}' must reference exactly one step.");
        }
    }

    private static void ValidateSteps(
        WorkflowMap map,
        List<string> errors)
    {
        var duplicateKeys = map.Steps
            .GroupBy(
                step => step.Key,
                StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        foreach (var key in duplicateKeys)
        {
            errors.Add(
                $"Step key '{key}' is duplicated.");
        }

        var stepKeys = map.Steps
            .Select(step => step.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (!stepKeys.Contains(map.StartStep))
        {
            errors.Add(
                $"start_step '{map.StartStep}' does not exist.");
        }

        foreach (var step in map.Steps)
        {
            switch (step)
            {
                case AutomaticStep automatic:
                    ValidateAutomaticStep(
                        automatic,
                        errors);
                    break;

                case WaitSignalStep waitSignal:
                    ValidateWaitSignalStep(
                        waitSignal,
                        errors);
                    break;

                case ManualStep manual:
                    ValidateManualStep(
                        manual,
                        errors);
                    break;

                case EndStep:
                    break;

                default:
                    errors.Add(
                        $"Unsupported step type for '{step.Key}'.");
                    break;
            }
        }
    }

    private static void ValidateAutomaticStep(
        AutomaticStep step,
        List<string> errors)
    {
        if (!string.Equals(
                step.Task.Service,
                "postgres",
                StringComparison.Ordinal))
        {
            errors.Add(
                $"Step '{step.Key}' must use service 'postgres'.");
        }

        ValidatePolicy(
            step.Task.RequiredPolicy,
            $"step '{step.Key}' task.required_policy",
            errors);

        ValidateTimeout(
            step.Task.TimeoutMs,
            $"step '{step.Key}' task.timeout_ms",
            errors);

        ValidateRetryPolicy(
            step.Task.Retry,
            $"step '{step.Key}'",
            errors);

        ValidateMapping(
            step.Task,
            step.Key,
            errors);
    }

    private static void ValidateWaitSignalStep(
        WaitSignalStep step,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(step.SignalType))
        {
            errors.Add(
                $"Step '{step.Key}' signal_type must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(step.Outcome))
        {
            errors.Add(
                $"Step '{step.Key}' outcome must not be empty.");
        }
    }

    private static void ValidateManualStep(
        ManualStep step,
        List<string> errors)
    {
        if (step.AllowedOutcomes.Count == 0)
        {
            errors.Add(
                $"Step '{step.Key}' must define at least one allowed outcome.");
        }

        if (step.AllowedOutcomes
            .GroupBy(value => value, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            errors.Add(
                $"Step '{step.Key}' contains duplicate allowed outcomes.");
        }
    }

    private static void ValidateTransitions(
        WorkflowMap map,
        List<string> errors)
    {
        var stepByKey = map.Steps
            .GroupBy(
                step => step.Key,
                StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.Single(),
                StringComparer.Ordinal);

        var duplicateTransitions = map.Transitions
            .GroupBy(
                transition => (
                    transition.From,
                    transition.Outcome),
                EqualityComparer<(string From, string Outcome)>.Default)
            .Where(group => group.Count() > 1);

        foreach (var duplicate in duplicateTransitions)
        {
            errors.Add(
                $"Multiple transitions exist for step '{duplicate.Key.From}' and outcome '{duplicate.Key.Outcome}'.");
        }

        foreach (var transition in map.Transitions)
        {
            if (!stepByKey.ContainsKey(transition.From))
            {
                errors.Add(
                    $"Transition source step '{transition.From}' does not exist.");
            }

            if (!stepByKey.ContainsKey(transition.To))
            {
                errors.Add(
                    $"Transition target step '{transition.To}' does not exist.");
            }
        }

        foreach (var endStep in map.Steps.OfType<EndStep>())
        {
            if (map.Transitions.Any(
                    transition => string.Equals(
                        transition.From,
                        endStep.Key,
                        StringComparison.Ordinal)))
            {
                errors.Add(
                    $"End step '{endStep.Key}' must not have outgoing transitions.");
            }
        }

        foreach (var step in map.Steps)
        {
            var expectedOutcomes = GetExpectedTransitionOutcomes(step);

            if (expectedOutcomes is null)
            {
                continue;
            }

            var actualTransitions = map.Transitions
                .Where(transition =>
                    string.Equals(
                        transition.From,
                        step.Key,
                        StringComparison.Ordinal))
                .GroupBy(
                    transition => transition.Outcome,
                    StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Count(),
                    StringComparer.Ordinal);

            foreach (var expectedOutcome in expectedOutcomes)
            {
                actualTransitions.TryGetValue(
                    expectedOutcome,
                    out var count);

                if (count == 0)
                {
                    errors.Add(
                        $"Step '{step.Key}' is missing transition for outcome '{expectedOutcome}'.");
                }
                else if (count > 1)
                {
                    errors.Add(
                        $"Step '{step.Key}' must have exactly one transition for outcome '{expectedOutcome}'.");
                }
            }

            var expectedSet = expectedOutcomes.ToHashSet(
                StringComparer.Ordinal);

            foreach (var actualOutcome in actualTransitions.Keys)
            {
                if (!expectedSet.Contains(actualOutcome))
                {
                    errors.Add(
                        $"Step '{step.Key}' has transition for undeclared outcome '{actualOutcome}'.");
                }
            }
        }
    }

    private static IReadOnlyList<string>? GetExpectedTransitionOutcomes(
        FlowStep step)
    {
        return step switch
        {
            AutomaticStep => null,
            WaitSignalStep waitSignal =>
                new[] { waitSignal.Outcome },
            ManualStep manual =>
                manual.AllowedOutcomes,
            EndStep => null,
            _ => null
        };
    }

    private static void ValidateGraph(
        WorkflowMap map,
        List<string> errors)
    {
        var stepKeys = map.Steps
            .Select(step => step.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (!stepKeys.Contains(map.StartStep))
        {
            return;
        }

        var adjacency = map.Transitions
            .Where(transition =>
                stepKeys.Contains(transition.From)
                && stepKeys.Contains(transition.To))
            .GroupBy(
                transition => transition.From,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(transition => transition.To)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var reachable = Traverse(
            map.StartStep,
            adjacency);

        foreach (var step in map.Steps)
        {
            if (!reachable.Contains(step.Key))
            {
                errors.Add(
                    $"Step '{step.Key}' is unreachable from start_step '{map.StartStep}'.");
            }
        }

        var endKeys = map.Steps
            .OfType<EndStep>()
            .Select(step => step.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (!reachable.Overlaps(endKeys))
        {
            errors.Add(
                "Workflow map must have at least one reachable end step.");
        }

        if (HasCycle(map, adjacency))
        {
            errors.Add(
                "Workflow map must be acyclic.");
        }

        var reachableEnds = reachable
            .Where(endKeys.Contains)
            .ToHashSet(StringComparer.Ordinal);

        if (reachableEnds.Count == 0)
        {
            errors.Add(
                "Workflow map has no reachable end step.");
            return;
        }

        foreach (var step in map.Steps)
        {
            if (step is EndStep)
            {
                continue;
            }

            if (!reachable.Contains(step.Key))
            {
                continue;
            }

            if (!CanReachEnd(
                    step.Key,
                    adjacency,
                    endKeys))
            {
                errors.Add(
                    $"Step '{step.Key}' leads to a dead end that cannot reach an end step.");
            }
        }
    }

    private static HashSet<string> Traverse(
        string start,
        IReadOnlyDictionary<string, string[]> adjacency)
    {
        var visited = new HashSet<string>(
            StringComparer.Ordinal);

        var queue = new Queue<string>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (!visited.Add(current))
            {
                continue;
            }

            if (!adjacency.TryGetValue(
                    current,
                    out var nextSteps))
            {
                continue;
            }

            foreach (var next in nextSteps)
            {
                queue.Enqueue(next);
            }
        }

        return visited;
    }

    private static bool HasCycle(
        WorkflowMap map,
        IReadOnlyDictionary<string, string[]> adjacency)
    {
        var state = map.Steps
            .Select(step => step.Key)
            .ToDictionary(
                key => key,
                _ => VisitState.Unvisited,
                StringComparer.Ordinal);

        foreach (var step in map.Steps)
        {
            if (state[step.Key] != VisitState.Unvisited)
            {
                continue;
            }

            if (HasCycleDfs(
                    step.Key,
                    adjacency,
                    state))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasCycleDfs(
        string current,
        IReadOnlyDictionary<string, string[]> adjacency,
        Dictionary<string, VisitState> state)
    {
        state[current] = VisitState.Visiting;

        if (adjacency.TryGetValue(
                current,
                out var nextSteps))
        {
            foreach (var next in nextSteps)
            {
                if (!state.ContainsKey(next))
                {
                    continue;
                }

                if (state[next] == VisitState.Visiting)
                {
                    return true;
                }

                if (state[next] == VisitState.Unvisited
                    && HasCycleDfs(
                        next,
                        adjacency,
                        state))
                {
                    return true;
                }
            }
        }

        state[current] = VisitState.Visited;

        return false;
    }

    private static bool CanReachEnd(
        string start,
        IReadOnlyDictionary<string, string[]> adjacency,
        IReadOnlySet<string> endKeys)
    {
        var visited = new HashSet<string>(
            StringComparer.Ordinal);

        var queue = new Queue<string>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (!visited.Add(current))
            {
                continue;
            }

            if (endKeys.Contains(current))
            {
                return true;
            }

            if (!adjacency.TryGetValue(
                    current,
                    out var nextSteps))
            {
                continue;
            }

            foreach (var next in nextSteps)
            {
                queue.Enqueue(next);
            }
        }

        return false;
    }

    
    private static void ValidateMappings(
        WorkflowMap map,
        List<string> errors)
    {
        foreach (var step in map.Steps.OfType<AutomaticStep>())
        {
            ValidateMapping(
                step.Task,
                step.Key,
                errors);
        }
    }

    private static void ValidateMapping(
        TaskDefinition task,
        string stepKey,
        List<string> errors)
    {
        if (task.InputConstants.ValueKind != JsonValueKind.Object)
        {
            errors.Add(
                $"Step '{stepKey}' input_constants must be a JSON object.");

            return;
        }

        var mappingTargets = new List<string>();

        foreach (var mapping in task.InputMapping)
        {
            var target = mapping.Key;
            var source = mapping.Value;

            if (!IsValidJsonPointer(target))
            {
                errors.Add(
                    $"Step '{stepKey}' has invalid target JSON Pointer '{target}'.");
            }

            if (!IsValidJsonPointer(source))
            {
                errors.Add(
                    $"Step '{stepKey}' has invalid source JSON Pointer '{source}'.");
            }

            if (IsValidJsonPointer(target))
            {
                mappingTargets.Add(target);
            }
        }

        for (var i = 0; i < mappingTargets.Count; i++)
        {
            for (var j = i + 1; j < mappingTargets.Count; j++)
            {
                if (PointersOverlap(
                        mappingTargets[i],
                        mappingTargets[j]))
                {
                    errors.Add(
                        $"Step '{stepKey}' contains overlapping input_mapping targets '{mappingTargets[i]}' and '{mappingTargets[j]}'.");
                }
            }
        }

        foreach (var constant in task.InputConstants.EnumerateObject())
        {
            var constantPointer = "/" + EscapeJsonPointerToken(
                constant.Name);

            foreach (var target in mappingTargets)
            {
                if (PointersOverlap(
                        constantPointer,
                        target))
                {
                    errors.Add(
                        $"Step '{stepKey}' input_constants property '{constant.Name}' overlaps mapping target '{target}'.");
                }
            }
        }
    }

    private static bool IsValidJsonPointer(
        string pointer)
    {
        if (string.IsNullOrEmpty(pointer)
            || pointer[0] != '/')
        {
            return false;
        }

        for (var i = 1; i < pointer.Length; i++)
        {
            if (pointer[i] != '~')
            {
                continue;
            }

            if (i + 1 >= pointer.Length)
            {
                return false;
            }

            var next = pointer[i + 1];

            if (next != '0' && next != '1')
            {
                return false;
            }

            i++;
        }

        return true;
    }

    private static bool PointersOverlap(
        string left,
        string right)
    {
        var leftTokens = ParsePointerTokens(left);
        var rightTokens = ParsePointerTokens(right);

        var minLength = Math.Min(
            leftTokens.Count,
            rightTokens.Count);

        for (var i = 0; i < minLength; i++)
        {
            if (!string.Equals(
                    leftTokens[i],
                    rightTokens[i],
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<string> ParsePointerTokens(
        string pointer)
    {
        return pointer
            .Split(
                '/',
                StringSplitOptions.None)
            .Skip(1)
            .Select(UnescapeJsonPointerToken)
            .ToArray();
    }

    private static string UnescapeJsonPointerToken(
        string token)
    {
        return token
            .Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal);
    }

    private static string EscapeJsonPointerToken(
        string token)
    {
        return token
            .Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);
    }

    private static void ValidateRetryPolicies(
        WorkflowMap map,
        List<string> errors)
    {
        foreach (var step in map.Steps.OfType<AutomaticStep>())
        {
            ValidateRetryPolicy(
                step.Task.Retry,
                $"step '{step.Key}'",
                errors);
        }
    }


    private static void ValidateRetryPolicy(
        RetryPolicy retry,
        string location,
        List<string> errors)
    {
        if (retry.MaxAttempts is < 1 or > 10)
        {
            errors.Add(
                $"{location} retry.max_attempts must be between 1 and 10.");
        }

        if (retry.DelaysMs.Count != retry.MaxAttempts - 1)
        {
            errors.Add(
                $"{location} retry.delays_ms must contain exactly max_attempts - 1 values.");
        }

        if (retry.DelaysMs.Any(
                delay => delay is < 0 or > 30000))
        {
            errors.Add(
                $"{location} retry.delays_ms values must be between 0 and 30000.");
        }
    }

    private static void ValidateTimeout(
        int timeoutMs,
        string location,
        List<string> errors)
    {
        if (timeoutMs is < 1 or > 30000)
        {
            errors.Add(
                $"{location} must be between 1 and 30000 milliseconds.");
        }
    }

    private static void ValidatePolicy(
        IReadOnlyList<string> policy,
        string location,
        List<string> errors)
    {
        var duplicates = policy
            .GroupBy(value => value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var duplicate in duplicates)
        {
            errors.Add(
                $"{location} contains duplicate scope '{duplicate}'.");
        }
    }

    private async Task ValidateActionsAndPoliciesAsync(
        WorkflowMap map,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        foreach (var step in map.Steps.OfType<AutomaticStep>())
        {
            var task = step.Task;

            ValidateTaskTargetIdentifier(
                task.Module,
                $"step '{step.Key}' task.module",
                errors);

            ValidateTaskTargetIdentifier(
                task.Action,
                $"step '{step.Key}' task.action",
                errors);

            if (task.ActionVersion < 1)
            {
                errors.Add(
                    $"Step '{step.Key}' task.action_version must be >= 1.");
            }

            var action = await _actions.GetAsync(
                task.Module,
                task.Action,
                task.ActionVersion,
                cancellationToken);

            if (action is null)
            {
                errors.Add(
                    $"Step '{step.Key}' references unknown action '{task.Module}.{task.Action}' version {task.ActionVersion}.");

                continue;
            }

            if (!action.Enabled)
            {
                errors.Add(
                    $"Step '{step.Key}' references disabled action '{task.Module}.{task.Action}' version {task.ActionVersion}.");
            }

            var taskPolicy = task.RequiredPolicy.ToHashSet(
                StringComparer.Ordinal);

            var actionPolicy = action.RequiredPolicy.ToHashSet(
                StringComparer.Ordinal);

            if (!taskPolicy.SetEquals(actionPolicy))
            {
                errors.Add(
                    $"Step '{step.Key}' task.required_policy must exactly match action '{task.Module}.{task.Action}' version {task.ActionVersion} required_policy.");
            }

            foreach (var requiredScope in taskPolicy)
            {
                if (!WorkerScopes.Contains(requiredScope))
                {
                    errors.Add(
                        $"Step '{step.Key}' requires scope '{requiredScope}', which is not granted to {RequiredWorkerPrincipal}.");
                }
            }


            if (!string.Equals(
                    action.HttpMethod,
                    "POST",
                    StringComparison.Ordinal))
            {
                errors.Add(
                    $"Action '{task.Module}.{task.Action}' version {task.ActionVersion} must use POST.");
            }

            ValidateTaskTargetIdentifier(
                task.Module,
                $"step '{step.Key}' task.module",
                errors);

        }
    }

    private static void ValidateTaskTargetIdentifier(
        string value,
        string location,
        List<string> errors)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > 63
            || !char.IsAsciiLetterLower(value[0]))
        {
            errors.Add(
                $"{location} is not a valid SQL identifier.");
            return;
        }

        for (var i = 1; i < value.Length; i++)
        {
            var character = value[i];

            if (!(
                    char.IsAsciiLetterLower(character)
                    || char.IsAsciiDigit(character)
                    || character == '_'))
            {
                errors.Add(
                    $"{location} is not a valid SQL identifier.");

                return;
            }
        }
    }

    private enum VisitState
    {
        Unvisited,
        Visiting,
        Visited
    }
}