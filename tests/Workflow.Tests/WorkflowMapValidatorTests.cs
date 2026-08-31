using ModuleDev.Workflow;
using Workflow.Tests.Builders;
using Workflow.Tests.Fakes;

namespace Workflow.Tests;

public sealed class WorkflowMapValidatorTests
{
    private readonly WorkflowMapValidator _validator;


    public WorkflowMapValidatorTests()
    {
        var catalog =
            new FakeActionCatalog();

        catalog.Add(
            FakeActions.Canary());


        _validator =
            new WorkflowMapValidator(
                FakeSchema.WorkflowMap(),
                catalog);
    }


    [Fact]
    public async Task Valid_automatic_flow_should_pass()
    {
        var json =
            new WorkflowMapBuilder()
                .AddAutomatic()
                .AddEnd("end")
                .Transition(
                    "start",
                    "APPLIED",
                    "end")
                .Build();

        var result =
            await _validator.ValidateAsync(json);

        Assert.True(
            result.IsValid,
            string.Join(
                Environment.NewLine,
                result.Errors));
    }



    [Fact]
    public async Task Duplicate_step_keys_should_fail()
    {
        var json =
            new WorkflowMapBuilder()
                .AddAutomatic()
                .DuplicateStep("start")
                .Build();


        var result =
            await _validator.ValidateAsync(json);


        Assert.False(result.IsValid);
    }


    [Fact]
    public async Task Missing_start_step_should_fail()
    {
        var json =
            new WorkflowMapBuilder()
                .RemoveStart()
                .Build();


        var result =
            await _validator.ValidateAsync(json);


        Assert.False(result.IsValid);
    }


    [Fact]
    public async Task Cycle_should_fail()
    {
        var json =
            new WorkflowMapBuilder()
                .AddAutomatic()
                .Transition(
                    "start",
                    "APPLIED",
                    "start")
                .Build();


        var result =
            await _validator.ValidateAsync(json);


        Assert.False(result.IsValid);
    }


    [Fact]
    public async Task Non_end_dead_path_should_fail()
    {
        var json =
            new WorkflowMapBuilder()
                .AddAutomatic("middle")
                .Transition(
                    "start",
                    "APPLIED",
                    "middle")
                .Build();


        var result =
            await _validator.ValidateAsync(json);


        Assert.False(result.IsValid);
    }


    [Fact]
    public async Task Unreachable_step_should_fail()
    {
        var json =
            new WorkflowMapBuilder()
                .AddEnd("orphan")
                .Build();


        var result =
            await _validator.ValidateAsync(json);


        Assert.False(result.IsValid);
    }


    [Fact]
    public async Task Missing_end_should_fail()
    {
        var json =
            new WorkflowMapBuilder()
                .RemoveEnd()
                .AddAutomatic()
                .Build();


        var result =
            await _validator.ValidateAsync(json);


        Assert.False(result.IsValid);
    }


    [Fact]
    public async Task Manual_step_without_transition_should_fail()
    {
        var json =
            new WorkflowMapBuilder()
                .AddManual(
                    "review",
                    "APPROVED")
                .Transition(
                    "start",
                    "APPLIED",
                    "review")
                .Build();


        var result =
            await _validator.ValidateAsync(json);


        Assert.False(result.IsValid);
    }


    [Fact]
    public async Task Automatic_step_missing_outcome_transition_should_fail()
    {
        var json =
            new WorkflowMapBuilder()
                .AddAutomaticWithoutTransitions()
                .AddEnd("end")
                .Build();


        var result =
            await _validator.ValidateAsync(json);


        Assert.False(
            result.IsValid,
            string.Join(
                Environment.NewLine,
                result.Errors));
    }


    [Fact]
    public async Task Automatic_step_duplicate_outcome_transition_should_fail()
    {
        var json =
            new WorkflowMapBuilder()
                .AddAutomatic()
                .AddEnd("end")
                .AddEnd("end2")
                .Transition(
                    "start",
                    "APPLIED",
                    "end")
                .Transition(
                    "start",
                    "APPLIED",
                    "end2")
                .Build();


        var result =
            await _validator.ValidateAsync(json);


        Assert.False(
            result.IsValid,
            string.Join(
                Environment.NewLine,
                result.Errors));
    }


   [Fact]
    public async Task Manual_step_does_not_require_runtime_completion_command()
    {
        var json =
            new WorkflowMapBuilder()
                .AddAutomatic("start")
                .AddManual(
                    "review",
                    "APPROVED")
                .AddEnd("end")
                .Transition(
                    "start",
                    "APPLIED",
                    "review")
                .Transition(
                    "review",
                    "APPROVED",
                    "end")
                .Build();


        var result =
            await _validator.ValidateAsync(json);


        Assert.True(
            result.IsValid,
            string.Join(
                Environment.NewLine,
                result.Errors));
    }


    [Fact]
    public async Task Unknown_action_should_fail()
    {
        var json =
            new WorkflowMapBuilder()
                .AddAutomatic()
                .WithUnknownAction()
                .Transition(
                    "start",
                    "APPLIED",
                    "end")
                .Build();


        var result =
            await _validator.ValidateAsync(json);


        Assert.False(result.IsValid);
    }


    [Fact]
    public async Task Disabled_action_should_fail()
    {
        var catalog =
            new FakeActionCatalog();

        catalog.Add(
            FakeActions.DisabledCanary());


        var validator =
            new WorkflowMapValidator(
                FakeSchema.WorkflowMap(),
                catalog);


        var json =
            new WorkflowMapBuilder()
                .AddAutomatic()
                .Transition(
                    "start",
                    "APPLIED",
                    "end")
                .Build();


        var result =
            await validator.ValidateAsync(json);


        Assert.False(result.IsValid);
    }


    [Fact]
    public async Task Action_policy_mismatch_should_fail()
    {
        var json =
            new WorkflowMapBuilder()
                .AddAutomatic()
                .WithPolicyMismatch()
                .Transition(
                    "start",
                    "APPLIED",
                    "end")
                .Build();


        var result =
            await _validator.ValidateAsync(json);


        Assert.False(result.IsValid);
    }


    [Fact]
    public async Task Retry_policy_wrong_delay_count_should_fail()
    {
        var json =
            new WorkflowMapBuilder()
                .AddAutomatic()
                .WithBadRetry()
                .Transition(
                    "start",
                    "APPLIED",
                    "end")
                .Build();


        var result =
            await _validator.ValidateAsync(json);


        Assert.False(result.IsValid);
    }


    [Fact]
    public async Task Zero_timeout_should_fail()
    {
        var json =
            new WorkflowMapBuilder()
                .AddAutomatic()
                .WithTimeout(0)
                .Transition(
                    "start",
                    "APPLIED",
                    "end")
                .Build();


        var result =
            await _validator.ValidateAsync(json);


        Assert.False(result.IsValid);
    }


    [Fact]
    public async Task Transition_from_end_should_fail()
    {
        var json =
            new WorkflowMapBuilder()
                .AddAutomatic()
                .Transition(
                    "start",
                    "APPLIED",
                    "end")
                .Transition(
                    "end",
                    "COMPLETED",
                    "start")
                .Build();


        var result =
            await _validator.ValidateAsync(json);


        Assert.False(result.IsValid);
    }


    [Fact]
    public async Task Invalid_mapping_pointer_should_fail()
    {
        var json =
            new WorkflowMapBuilder()
                .AddAutomatic()
                .WithInvalidMapping()
                .Transition(
                    "start",
                    "APPLIED",
                    "end")
                .Build();


        var result =
            await _validator.ValidateAsync(json);


        Assert.False(result.IsValid);
    }


    [Fact]
    public async Task Manual_step_with_allowed_outcome_transition_should_pass()
    {
        var json =
            new WorkflowMapBuilder()
                .AddAutomatic("start")
                .AddManual(
                    "review",
                    "APPROVED")
                .AddEnd("end")
                .Transition(
                    "start",
                    "APPLIED",
                    "review")
                .Transition(
                    "review",
                    "APPROVED",
                    "end")
                .Build();


        var result =
            await _validator.ValidateAsync(json);


        Assert.True(
            result.IsValid,
            string.Join(
                Environment.NewLine,
                result.Errors));
    }


    [Fact]
    public async Task Manual_step_missing_allowed_outcome_transition_should_fail()
    {
        var json =
            new WorkflowMapBuilder()
                .AddAutomatic("start")
                .AddManual(
                    "review",
                    "APPROVED")
                .AddEnd("end")
                .Transition(
                    "start",
                    "APPLIED",
                    "review")
                .Build();


        var result =
            await _validator.ValidateAsync(json);


        Assert.False(
            result.IsValid,
            string.Join(
                Environment.NewLine,
                result.Errors));
    }

}