using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModuleDev.Cli.Output;

public sealed class CliOutput
{
    private readonly string _contractVersion;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public CliOutput(string contractVersion)
    {
        _contractVersion = contractVersion;
    }

    public int Success(object result)
    {
        var response = new
        {
            status = "ok",
            result,
            meta = new
            {
                contractVersion = _contractVersion
            }
        };

        WriteJson(response);

        return 0;
    }

    public int Error(
        string code,
        string message,
        object? details = null)
    {
        var response = new
        {
            status = "error",
            code,
            message,
            details = details ?? new { },
            meta = new
            {
                contractVersion = _contractVersion
            }
        };

        WriteJson(response);

        return 1;
    }

    public void WriteDiagnostic(string message)
    {
        Console.Error.WriteLine(message);
    }

    public void WriteDiagnostic(Exception exception)
    {
        Console.Error.WriteLine(exception.ToString());
    }

    private static void WriteJson(object value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);

        Console.Out.WriteLine(json);
    }
}