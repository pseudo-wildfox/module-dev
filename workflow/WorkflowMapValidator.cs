using System.Text.Json;
using Json.Schema;
using ModuleDev.Contracts;

namespace ModuleDev.Workflow;

public sealed class WorkflowMapValidator
{
    private const string ExpectedContractVersion = "course-1";
    private const string RequiredWorkerPrincipal = "workflow-worker";
    private const string RequiredTaskService = "postgres";

    // Server-side scopes, available to workflow-worker.
    //
    // Hidden actions may require any subset of these scopes.
    // The validator must never derive worker authority from the workflow map.
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

            // The JSON Schema is authoritative for:
            // - unknown fields
            // - primitive types
            // - required fields
            // - key syntax
            // - enum-like syntax
            // - basic numeric bounds
            //
            // Semantic checks below intentionally happen only
            // after the document passes the course-1 schema.
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

            ValidateTopLevel(
                map,
                errors);

            ValidateSteps(
                map,
                errors);

            ValidateTransitionStructure(
                map,
                errors);

            ValidateNonAutomaticOutcomeCoverage(
                map,
                errors);

            ValidateGraph(
                map,
                errors);

            ValidateMappings(
                map,
                errors);

            ValidateRetryPolicies(
                map,
                errors);

            await ValidateAutomaticStepsAsync(
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
            errors.Add(
                "flow_name must not be empty.");
        }

        if (map.Version < 1)
        {
            errors.Add(
                "version must be >= 1.");
        }

        if (map.Steps.Count < 2)
        {
            errors.Add(
                "workflow map must contain at least two steps.");
        }

        if (map.Transitions.Count == 0)
        {
            errors.Add(
                "workflow map must contain at least one transition.");
        }

        var startStepCount = map.Steps.Count(
            step => string.Equals(
                step.Key,
                map.StartStep,
                StringComparison.Ordinal));

        if (startStepCount != 1)
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
            .OrderBy(
                key => key,
                StringComparer.Ordinal)
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
                    ValidateAutomaticStepShape(
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

    private static void ValidateAutomaticStepShape(
        AutomaticStep step,
        List<string> errors)
    {
        if (!string.Equals(
                step.Task.Service,
                RequiredTaskService,
                StringComparison.Ordinal))
        {
            errors.Add(
                $"Step '{step.Key}' must use service '{RequiredTaskService}'.");
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
            step.Key,
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

        var duplicateOutcomes = step.AllowedOutcomes
            .GroupBy(
                value => value,
                StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(
                value => value,
                StringComparer.Ordinal);

        foreach (var outcome in duplicateOutcomes)
        {
            errors.Add(
                $"Step '{step.Key}' contains duplicate allowed outcome '{outcome}'.");
        }
    }

    private static void ValidateTransitionStructure(
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
                    transition.Outcome))
            .Where(group => group.Count() > 1);

        foreach (var duplicate in duplicateTransitions)
        {
            errors.Add(
                $"Multiple transitions exist for step '{duplicate.Key.From}' " +
                $"and outcome '{duplicate.Key.Outcome}'.");
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
            var outgoing = map.Transitions.Any(
                transition => string.Equals(
                    transition.From,
                    endStep.Key,
                    StringComparison.Ordinal));

            if (outgoing)
            {
                errors.Add(
                    $"End step '{endStep.Key}' must not have outgoing transitions.");
            }
        }
    }

    private static void ValidateNonAutomaticOutcomeCoverage(
        WorkflowMap map,
        List<string> errors)
    {
        foreach (var step in map.Steps)
        {
            switch (step)
            {
                case WaitSignalStep waitSignal:
                    ValidateTransitionCoverage(
                        waitSignal.Key,
                        new[] { waitSignal.Outcome },
                        map.Transitions,
                        errors);
                    break;

                case ManualStep manual:
                    ValidateTransitionCoverage(
                        manual.Key,
                        manual.AllowedOutcomes,
                        map.Transitions,
                        errors);
                    break;
            }
        }
    }

    private static void ValidateTransitionCoverage(
        string stepKey,
        IReadOnlyList<string> expectedOutcomes,
        IReadOnlyList<Transition> transitions,
        List<string> errors)
    {
        var transitionsByOutcome = transitions
            .Where(
                transition => string.Equals(
                    transition.From,
                    stepKey,
                    StringComparison.Ordinal))
            .GroupBy(
                transition => transition.Outcome,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);

        var expectedSet = expectedOutcomes.ToHashSet(
            StringComparer.Ordinal);

        foreach (var expectedOutcome in expectedSet)
        {
            transitionsByOutcome.TryGetValue(
                expectedOutcome,
                out var count);

            if (count == 0)
            {
                errors.Add(
                    $"Step '{stepKey}' is missing transition " +
                    $"for outcome '{expectedOutcome}'.");
            }
            else if (count > 1)
            {
                errors.Add(
                    $"Step '{stepKey}' must have exactly one transition " +
                    $"for outcome '{expectedOutcome}'.");
            }
        }

        foreach (var actualOutcome in transitionsByOutcome.Keys)
        {
            if (!expectedSet.Contains(actualOutcome))
            {
                errors.Add(
                    $"Step '{stepKey}' has transition for undeclared " +
                    $"outcome '{actualOutcome}'.");
            }
        }
    }

    private static void ValidateGraph(
        WorkflowMap map,
        List<string> errors)
    {
        var duplicateKeys = map.Steps
            .GroupBy(
                step => step.Key,
                StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        // Without unique step keys the graph is ambiguous.
        if (duplicateKeys.Length > 0)
        {
            return;
        }

        var stepKeys = map.Steps
            .Select(step => step.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (!stepKeys.Contains(map.StartStep))
        {
            return;
        }

        var adjacency = map.Transitions
            .Where(
                transition =>
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
                    $"Step '{step.Key}' is unreachable from " +
                    $"start_step '{map.StartStep}'.");
            }
        }

        var endKeys = map.Steps
            .OfType<EndStep>()
            .Select(step => step.Key)
            .ToHashSet(StringComparer.Ordinal);

        var reachableEndExists =
            reachable.Overlaps(endKeys);

        if (!reachableEndExists)
        {
            errors.Add(
                "Workflow map must have at least one reachable end step.");
        }

        if (HasCycle(
                map,
                adjacency))
        {
            errors.Add(
                "Workflow map must be acyclic.");
        }

        if (!reachableEndExists)
        {
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
                    $"Step '{step.Key}' leads to a dead end that " +
                    "cannot reach an end step.");
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

            var targetValid = IsValidJsonPointer(target);
            var sourceValid = IsValidJsonPointer(source);

            if (!targetValid)
            {
                errors.Add(
                    $"Step '{stepKey}' has invalid target " +
                    $"JSON Pointer '{target}'.");
            }

            if (!sourceValid)
            {
                errors.Add(
                    $"Step '{stepKey}' has invalid source " +
                    $"JSON Pointer '{source}'.");
            }

            if (targetValid)
            {
                mappingTargets.Add(target);
            }
        }

        for (var i = 0; i < mappingTargets.Count; i++)
        {
            for (var j = i + 1; j < mappingTargets.Count; j++)
            {
                var left = mappingTargets[i];
                var right = mappingTargets[j];

                if (PointersOverlap(left, right))
                {
                    errors.Add(
                        $"Step '{stepKey}' contains overlapping " +
                        $"input_mapping targets '{left}' and '{right}'.");
                }
            }
        }

        foreach (var constant in task.InputConstants.EnumerateObject())
        {
            var constantPointer =
                "/" + EscapeJsonPointerToken(constant.Name);

            foreach (var target in mappingTargets)
            {
                if (PointersOverlap(
                        constantPointer,
                        target))
                {
                    errors.Add(
                        $"Step '{stepKey}' input_constants property " +
                        $"'{constant.Name}' overlaps mapping target '{target}'.");
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

            if (next != '0'
                && next != '1')
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

        // Same pointer or one pointer is an ancestor of the other.
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
            .Replace(
                "~1",
                "/",
                StringComparison.Ordinal)
            .Replace(
                "~0",
                "~",
                StringComparison.Ordinal);
    }

    private static string EscapeJsonPointerToken(
        string token)
    {
        return token
            .Replace(
                "~",
                "~0",
                StringComparison.Ordinal)
            .Replace(
                "/",
                "~1",
                StringComparison.Ordinal);
    }

    private static void ValidateRetryPolicies(
        WorkflowMap map,
        List<string> errors)
    {
        foreach (var step in map.Steps.OfType<AutomaticStep>())
        {
            ValidateRetryPolicy(
                step.Task.Retry,
                step.Key,
                errors);
        }
    }

    private static void ValidateRetryPolicy(
        RetryPolicy retry,
        string stepKey,
        List<string> errors)
    {
        var maxAttemptsValid =
            retry.MaxAttempts is >= 1 and <= 10;

        if (!maxAttemptsValid)
        {
            errors.Add(
                $"Step '{stepKey}' retry.max_attempts " +
                "must be between 1 and 10.");
        }

        if (retry.DelaysMs.Any(
                delay => delay is < 0 or > 30000))
        {
            errors.Add(
                $"Step '{stepKey}' retry.delays_ms values " +
                "must be between 0 and 30000.");
        }

        if (maxAttemptsValid
            && retry.DelaysMs.Count != retry.MaxAttempts - 1)
        {
            errors.Add(
                $"Step '{stepKey}' retry.delays_ms must contain " +
                "exactly max_attempts - 1 values.");
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
            .GroupBy(
                value => value,
                StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(
                value => value,
                StringComparer.Ordinal);

        foreach (var duplicate in duplicates)
        {
            errors.Add(
                $"{location} contains duplicate scope '{duplicate}'.");
        }
    }

    private async Task ValidateAutomaticStepsAsync(
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
                    $"Step '{step.Key}' references unknown action " +
                    $"'{task.Module}.{task.Action}' " +
                    $"version {task.ActionVersion}.");

                continue;
            }

            if (!action.Enabled)
            {
                errors.Add(
                    $"Step '{step.Key}' references disabled action " +
                    $"'{task.Module}.{task.Action}' " +
                    $"version {task.ActionVersion}.");
            }

            ValidateExactPolicy(
                task.RequiredPolicy,
                action.RequiredPolicy,
                step.Key,
                errors);

            ValidateWorkerScopes(
                task.RequiredPolicy,
                step.Key,
                errors);

            ValidateActionMethod(
                action,
                step.Key,
                errors);

            ValidateAutomaticOutcomeCoverage(
                step,
                action,
                map.Transitions,
                errors);
        }
    }

    private static void ValidateAutomaticOutcomeCoverage(
        AutomaticStep step,
        ActionManifest action,
        IReadOnlyList<Transition> transitions,
        List<string> errors)
    {
        ValidateTransitionCoverage(
            step.Key,
            action.Outcomes,
            transitions,
            errors);
    }

    private static void ValidateExactPolicy(
        IReadOnlyList<string> taskPolicy,
        IReadOnlyList<string> actionPolicy,
        string stepKey,
        List<string> errors)
    {
        var taskSet = taskPolicy.ToHashSet(
            StringComparer.Ordinal);

        var actionSet = actionPolicy.ToHashSet(
            StringComparer.Ordinal);

        if (!taskSet.SetEquals(actionSet))
        {
            errors.Add(
                $"Step '{stepKey}' task.required_policy must exactly " +
                "match the referenced action required_policy.");
        }
    }

    private static void ValidateWorkerScopes(
        IReadOnlyList<string> requiredPolicy,
        string stepKey,
        List<string> errors)
    {
        foreach (var requiredScope in requiredPolicy)
        {
            if (!WorkerScopes.Contains(requiredScope))
            {
                errors.Add(
                    $"Step '{stepKey}' requires scope '{requiredScope}', " +
                    $"which is not granted to {RequiredWorkerPrincipal}.");
            }
        }
    }

    private static void ValidateActionMethod(
        ActionManifest action,
        string stepKey,
        List<string> errors)
    {
        if (!string.Equals(
                action.HttpMethod,
                "POST",
                StringComparison.Ordinal))
        {
            errors.Add(
                $"Action '{action.Module}.{action.Action}' " +
                $"version {action.Version} must use POST.");
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

            if (char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character == '_')
            {
                continue;
            }

            errors.Add(
                $"{location} is not a valid SQL identifier.");

            return;
        }
    }

    private enum VisitState
    {
        Unvisited,
        Visiting,
        Visited
    }
}