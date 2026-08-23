using ModuleDev.Cli.Commands;
using ModuleDev.Cli.Database;
using ModuleDev.Cli.Migrations;
using ModuleDev.Cli.Output;

const string contractVersion = "course-1";

var output = new CliOutput(contractVersion);
var database = new CliDatabase();
var migrations = new MigrationService(database, output);
var dispatcher = new CommandDispatcher(migrations, output);

try
{
    return await dispatcher.ExecuteAsync(args);
}
catch (Exception ex)
{
    output.WriteDiagnostic(ex);

    return output.Error(
        code: "internal.error",
        message: "unexpected CLI error");
}