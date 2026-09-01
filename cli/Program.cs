using ModuleDev.Cli.Commands;
using ModuleDev.Cli.Database;
using ModuleDev.Cli.Migrations;
using ModuleDev.Cli.Output;
using ModuleDev.Contracts.Validation;
using ModuleDev.Workflow;


const string contractVersion = "course-1";


var output = new CliOutput(contractVersion);



var schemaPath = Path.Combine(
    AppContext.BaseDirectory,
    "contracts",
    "course-1",
    "action-manifest.schema.json");


var schemaJson =
    await File.ReadAllTextAsync(schemaPath);


var validator =
    new ActionManifestValidator(schemaJson);



var database = new CliDatabase();


var migrations =
    new MigrationService(
        database,
        output);


var actionValidate =
    new ActionValidateCommand(
        validator,
        output);


var actionPublish =
    new ActionPublishCommand(
        database,
        validator,
        output);


var actionList =
    new ActionListCommand(
        database,
        output);


var actionActivate =
    new ActionActivateCommand(
        database,
        output);


var actionDisable =
    new ActionDisableCommand(
        database,
        output);



var workflowSchemaPath = Path.Combine(
    AppContext.BaseDirectory,
    "contracts",
    "course-1",
    "workflow-map.schema.json");


var workflowSchemaJson =
    await File.ReadAllTextAsync(workflowSchemaPath);


var actionCatalog =
    new PostgresActionCatalog(database);


var workflowValidator =
    new WorkflowMapValidator(
        workflowSchemaJson,
        actionCatalog);


var flowValidate =
    new FlowValidateCommand(
        workflowValidator,
        output);


var flowPublish =
    new FlowPublishCommand(
        database,
        workflowValidator,
        output);

var flowList =
    new FlowListCommand(
        database,
        output);

var flowActivate =
    new FlowActivateCommand(
        database,
        output);

var flowStart =
    new FlowStartCommand(
        database,
        output);

var flowGet =
    new FlowGetCommand(
        database,
        output);


var dispatcher =
    new CommandDispatcher(
        migrations,
        actionValidate,
        actionPublish,
        actionList,
        actionActivate,
        actionDisable,
        flowValidate,
        flowPublish,
        flowList,
        flowActivate,
        flowStart,
        flowGet,
        output);



try
{
    return await dispatcher.ExecuteAsync(args);
}
catch (Exception ex)
{
    output.WriteDiagnostic(ex);

    return output.Error(
        "internal.error",
        "unexpected CLI error");
}