using System.Text.Json;
using System.Text.Json.Nodes;
using ModuleDev.Api.Database;
using ModuleDev.Api.Security;
using ModuleDev.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace ModuleDev.Api.Runtime;

public sealed class ActionExecutor
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly ActionResponseValidator _responseValidator;
    private readonly IdempotencyStore _idempotencyStore;

    public ActionExecutor(
        DbConnectionFactory connectionFactory,
        ActionResponseValidator responseValidator,
    IdempotencyStore idempotencyStore)
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
        CancellationToken cancellationToken)
    {
        await using var connection =
            await _connectionFactory.OpenAsync(
                cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        using var contextJson =
            JsonSerializer.SerializeToDocument(context);

        string? scopeKey = null;

        try
        {
            if (manifest.IdempotencyMode == "required" &&
                string.IsNullOrWhiteSpace(idempotencyKey))
            {
                throw new ActionExecutionException(
                    "Idempotency-Key is required.");
            }

            if (manifest.IdempotencyMode != "none" &&
                !string.IsNullOrWhiteSpace(idempotencyKey))
            {
                scopeKey =
                    _idempotencyStore.BuildScopeKey(
                        manifest.IdempotencyScope,
                        context.Principal,
                        context.Consumer,
                        manifest.Module,
                        manifest.Action);

                var payloadHash =
                    _idempotencyStore.CalculatePayloadHash(
                        payload.RootElement);

                var reservation =
                    await _idempotencyStore.ReserveAsync(
                        connection,
                        transaction,
                        scopeKey,
                        idempotencyKey,
                        payloadHash,
                        cancellationToken);

                switch (reservation.Kind)
                {
                    case IdempotencyReservationKind.Conflict:

                        throw new IdempotencyConflictException();

                    case IdempotencyReservationKind.Replay:

                        await transaction.CommitAsync(
                            cancellationToken);

                        return reservation.ResponseJson
                            ?? throw new InvalidOperationException(
                                "Idempotency replay has no response.");

                    case IdempotencyReservationKind.New:
                        break;

                    default:
                        throw new InvalidOperationException(
                            "Unknown idempotency reservation state.");
                }
            }

            await using var command =
                new NpgsqlCommand(
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
                    transaction);

            command.Parameters.Add(
                new NpgsqlParameter(
                    "module",
                    NpgsqlDbType.Text)
                {
                    Value = manifest.Module
                });

            command.Parameters.Add(
                new NpgsqlParameter(
                    "action",
                    NpgsqlDbType.Text)
                {
                    Value = manifest.Action
                });

            command.Parameters.Add(
                new NpgsqlParameter(
                    "version",
                    NpgsqlDbType.Integer)
                {
                    Value = manifest.Version
                });

            command.Parameters.Add(
                new NpgsqlParameter(
                    "context",
                    NpgsqlDbType.Jsonb)
                {
                    Value = contextJson
                });

            command.Parameters.Add(
                new NpgsqlParameter(
                    "payload",
                    NpgsqlDbType.Jsonb)
                {
                    Value = payload
                });

            var value =
                await command.ExecuteScalarAsync(
                    cancellationToken);

            var responseJson =
    value?.ToString()
    ?? throw new InvalidOperationException(
        "api.invoke returned NULL.");

            using var response =
                JsonDocument.Parse(responseJson);

            var validation =
                _responseValidator.Validate(
                    manifest,
                    response.RootElement);

            if (!validation.IsValid)
            {
                throw new ActionExecutionException(
                    validation.ErrorMessage
                    ?? "action result violates the declared contract.");
            }

            var finalResponse =
                JsonNode.Parse(responseJson)?.AsObject()
                ?? throw new ActionExecutionException(
                    "action result is not a JSON object.");

            finalResponse["meta"] = new JsonObject
            {
                ["correlationId"] =
                    context.CorrelationId.ToString(),
                ["actionVersion"] =
                    manifest.Version
            };

            var finalResponseJson =
                finalResponse.ToJsonString();

            if (scopeKey is not null &&
                !string.IsNullOrWhiteSpace(idempotencyKey))
            {
                await _idempotencyStore.CompleteAsync(
                    connection,
                    transaction,
                    scopeKey,
                    idempotencyKey,
                    finalResponseJson,
                    cancellationToken);
            }

            await transaction.CommitAsync(
                cancellationToken);

            return finalResponseJson;
        }
        catch
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            throw;
        }
    }
}
