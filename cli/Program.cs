using ModuleDev.Cli.Commands;
using ModuleDev.Cli.Database;
using ModuleDev.Cli.Migrations;
using ModuleDev.Cli.Output;
using ModuleDev.Contracts.Validation;


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



var dispatcher =
    new CommandDispatcher(
        migrations,
        actionValidate,
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