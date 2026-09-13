using System.Text.Json;
using System.Text.Json.Nodes;
using ModuleDev.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace ModuleDev.ActionRuntime;

public sealed class ActionExecutor
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly ActionResponseValidator _responseValidator;
    private readonly IdempotencyStore _idempotencyStore;

    public ActionExecutor(
        DbConnectionFactory connectionFactory,
        ActionResponseValidator responseValidator,
        IdempotencyStore idempotencyStore
    )
    {
        _connectionFactory = connectionFactory;
        _responseValidator = responseValidator;
        _idempotencyStore = idempotencyStore;
    }

    public async Task<string> ExecuteAsync(
        ActionManifest manifest,
        TrustedContext context,
        JsonDocument payload,
        string? idempotencyKey,
        CancellationToken cancellationToken
    )
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var remaining = context.Deadline - DateTimeOffset.UtcNow;

        if (remaining <= TimeSpan.Zero)
        {
            throw new ActionTimeoutException();
        }

        timeoutCts.CancelAfter(remaining);

        var executionToken = timeoutCts.Token;

        await using var connection = await _connectionFactory.OpenAsync(executionToken);

        await using var transaction = await connection.BeginTransactionAsync(executionToken);

        try
        {
            var result = await ExecuteAsync(
                manifest,
                context,
                payload,
                idempotencyKey,
                connection,
                transaction,
                executionToken
            );

            await transaction.CommitAsync(executionToken);

            return result;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // The original exception is more important
                // than a rollback failure.
            }

            throw;
        }
    }

    public async Task<string> ExecuteAsync(
        ActionManifest manifest,
        TrustedContext context,
        JsonDocument payload,
        string? idempotencyKey,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        var remaining = context.Deadline - DateTimeOffset.UtcNow;

        if (remaining <= TimeSpan.Zero)
        {
            throw new ActionTimeoutException();
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeoutCts.CancelAfter(remaining);

        var executionToken = timeoutCts.Token;

        using var contextJson = JsonSerializer.SerializeToDocument(context);

        string? scopeKey = null;

        if (manifest.IdempotencyMode == "required" && string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ActionExecutionException("Idempotency-Key is required.");
        }

        if (manifest.IdempotencyMode != "none" && !string.IsNullOrWhiteSpace(idempotencyKey))
        {
            scopeKey = _idempotencyStore.BuildScopeKey(
                manifest.IdempotencyScope,
                context.Principal,
                context.Consumer,
                manifest.Module,
                manifest.Action
            );

            var payloadHash = _idempotencyStore.CalculatePayloadHash(payload.RootElement);

            var reservation = await _idempotencyStore.ReserveAsync(
                connection,
                transaction,
                scopeKey,
                idempotencyKey,
                payloadHash,
                executionToken
            );

            switch (reservation.Kind)
            {
                case IdempotencyReservationKind.Conflict:

                    throw new IdempotencyConflictException();

                case IdempotencyReservationKind.Replay:

                    return reservation.ResponseJson
                        ?? throw new InvalidOperationException(
                            "Idempotency replay has no response."
                        );

                case IdempotencyReservationKind.New:
                    break;

                default:
                    throw new InvalidOperationException("Unknown idempotency reservation state.");
            }
        }

        await using var command = new NpgsqlCommand(
            """
            SELECT api.invoke(
                @module,
                @action,
                @version,
                @context,
                @payload
            );
            """,
            connection,
            transaction
        );

        command.Parameters.Add(
            new NpgsqlParameter("module", NpgsqlDbType.Text) { Value = manifest.Module }
        );

        command.Parameters.Add(
            new NpgsqlParameter("action", NpgsqlDbType.Text) { Value = manifest.Action }
        );

        command.Parameters.Add(
            new NpgsqlParameter("version", NpgsqlDbType.Integer) { Value = manifest.Version }
        );

        command.Parameters.Add(
            new NpgsqlParameter("context", NpgsqlDbType.Jsonb) { Value = contextJson }
        );

        command.Parameters.Add(
            new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = payload }
        );

        var remainingForCommand = context.Deadline - DateTimeOffset.UtcNow;

        if (remainingForCommand <= TimeSpan.Zero)
        {
            throw new ActionTimeoutException();
        }

        command.CommandTimeout = Math.Clamp(
            (int)Math.Ceiling(remainingForCommand.TotalSeconds),
            1,
            30
        );

        try
        {
            var value = await command.ExecuteScalarAsync(executionToken);

            var responseJson =
                value?.ToString()
                ?? throw new InvalidOperationException("api.invoke returned NULL.");

            using var response = JsonDocument.Parse(responseJson);

            var validation = _responseValidator.Validate(manifest, response.RootElement);

            if (validation.IsDomainError)
            {
                var errorResponse = response.RootElement;

                var code = errorResponse.TryGetProperty("code", out var codeProperty)
                    ? codeProperty.GetString()
                    : null;

                var message = errorResponse.TryGetProperty("message", out var messageProperty)
                    ? messageProperty.GetString()
                    : null;

                var retryable =
                    errorResponse.TryGetProperty("retryable", out var retryableProperty)
                    && retryableProperty.ValueKind == JsonValueKind.True;

                var details = errorResponse.TryGetProperty("details", out var detailsProperty)
                    ? detailsProperty
                    : JsonSerializer.SerializeToElement(new { });

                throw new ActionDomainException(
                    code ?? "internal.error",
                    message ?? "action returned a domain error.",
                    retryable,
                    details
                );
            }

            if (!validation.IsValid)
            {
                throw new ActionExecutionException(
                    validation.ErrorMessage ?? "action result violates the declared contract."
                );
            }

            var finalResponse =
                JsonNode.Parse(responseJson)?.AsObject()
                ?? throw new ActionExecutionException("action result is not a JSON object.");

            finalResponse["meta"] = new JsonObject
            {
                ["correlationId"] = context.CorrelationId.ToString(),

                ["actionVersion"] = manifest.Version,
            };

            var finalResponseJson = finalResponse.ToJsonString();

            if (scopeKey is not null && !string.IsNullOrWhiteSpace(idempotencyKey))
            {
                await _idempotencyStore.CompleteAsync(
                    connection,
                    transaction,
                    scopeKey,
                    idempotencyKey,
                    finalResponseJson,
                    executionToken
                );
            }

            return finalResponseJson;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new ActionTimeoutException();
        }
    }

    
}
