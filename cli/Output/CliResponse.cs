namespace ModuleDev.Cli.Output;

public sealed record CliMeta(
    string ContractVersion);

public sealed record CliSuccessResponse<T>(
    string Status,
    T Result,
    CliMeta Meta);

public sealed record CliErrorResponse(
    string Status,
    string Code,
    string Message,
    object Details,
    CliMeta Meta);