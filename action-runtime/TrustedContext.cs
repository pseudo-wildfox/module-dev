using System.Text.Json.Serialization;

namespace ModuleDev.ActionRuntime;

public sealed record TrustedContext(
    [property: JsonPropertyName("principal")]
    string Principal,

    [property: JsonPropertyName("consumer")]
    string Consumer,

    [property: JsonPropertyName("scopes")]
    IReadOnlyList<string> Scopes,

    [property: JsonPropertyName("correlationId")]
    Guid CorrelationId,

    [property: JsonPropertyName("requestId")]
    string? RequestId,

    [property: JsonPropertyName("deadline")]
    DateTimeOffset Deadline);