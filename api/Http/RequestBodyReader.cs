using System.Text.Json;

namespace ModuleDev.Api.Http;

public sealed class RequestBodyReader
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow
    };

    public async Task<JsonDocument> ReadAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await JsonDocument.ParseAsync(
                request.Body,
                JsonOptions,
                cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidJsonRequestException(
                "request body contains invalid JSON.",
                exception);
        }
    }
}

public sealed class InvalidJsonRequestException : Exception
{
    public InvalidJsonRequestException(
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
    }
}