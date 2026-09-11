using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ModuleDev.ActionRuntime;

public sealed class IdempotencyStore
{
    public string CalculatePayloadHash(
        JsonElement payload)
    {
        var json = payload.GetRawText();

        var bytes = Encoding.UTF8.GetBytes(json);

        var hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash)
            .ToLowerInvariant();
    }

    public string BuildScopeKey(
        string scope,
        string principal,
        string consumer,
        string module,
        string action)
    {
        return scope switch
        {
            "principal_action" =>
                $"{principal}|{module}.{action}",

            "consumer_action" =>
                $"{consumer}|{module}.{action}",

            "global_action" =>
                $"{module}.{action}",

            _ => throw new InvalidOperationException(
                $"Unsupported idempotency scope: '{scope}'.")
        };
    }

    public async Task<IdempotencyReservation> ReserveAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string scopeKey,
        string idempotencyKey,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        const string insertSql = """
            INSERT INTO runtime.idempotency (
                scope_key,
                idempotency_key,
                payload_hash,
                status,
                response_json
            )
            VALUES (
                @scope_key,
                @idempotency_key,
                @payload_hash,
                'IN_PROGRESS',
                '{}'::jsonb
            )
            ON CONFLICT (
                scope_key,
                idempotency_key
            )
            DO NOTHING;
            """;

        await using var insert =
            new NpgsqlCommand(
                insertSql,
                connection,
                transaction);

        insert.Parameters.Add(
            new NpgsqlParameter(
                "scope_key",
                NpgsqlDbType.Text)
            {
                Value = scopeKey
            });

        insert.Parameters.Add(
            new NpgsqlParameter(
                "idempotency_key",
                NpgsqlDbType.Text)
            {
                Value = idempotencyKey
            });

        insert.Parameters.Add(
            new NpgsqlParameter(
                "payload_hash",
                NpgsqlDbType.Text)
            {
                Value = payloadHash
            });

        var affected =
            await insert.ExecuteNonQueryAsync(
                cancellationToken);

        if (affected == 1)
        {
            return IdempotencyReservation.New();
        }

        const string selectSql = """
            SELECT
                payload_hash,
                status,
                response_json
            FROM runtime.idempotency
            WHERE scope_key = @scope_key
              AND idempotency_key = @idempotency_key;
            """;

        await using var select =
            new NpgsqlCommand(
                selectSql,
                connection,
                transaction);

        select.Parameters.Add(
            new NpgsqlParameter(
                "scope_key",
                NpgsqlDbType.Text)
            {
                Value = scopeKey
            });

        select.Parameters.Add(
            new NpgsqlParameter(
                "idempotency_key",
                NpgsqlDbType.Text)
            {
                Value = idempotencyKey
            });

        await using var reader =
            await select.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Idempotency record disappeared after conflict.");
        }

        var existingHash =
            reader.GetString(0);

        var status =
            reader.GetString(1);

        var responseJson =
            reader.GetFieldValue<string>(2);

        if (!string.Equals(
                existingHash,
                payloadHash,
                StringComparison.Ordinal))
        {
            return IdempotencyReservation.Conflict();
        }

        if (!string.Equals(
                status,
                "COMPLETED",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Existing idempotency record is not completed.");
        }

        return IdempotencyReservation.Replay(
            responseJson);
    }

    public async Task CompleteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string scopeKey,
        string idempotencyKey,
        string responseJson,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE runtime.idempotency
            SET
                status = 'COMPLETED',
                response_json = CAST(@response_json AS jsonb)
            WHERE scope_key = @scope_key
              AND idempotency_key = @idempotency_key;
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.Add(
            new NpgsqlParameter(
                "scope_key",
                NpgsqlDbType.Text)
            {
                Value = scopeKey
            });

        command.Parameters.Add(
            new NpgsqlParameter(
                "idempotency_key",
                NpgsqlDbType.Text)
            {
                Value = idempotencyKey
            });

        command.Parameters.Add(
            new NpgsqlParameter(
                "response_json",
                NpgsqlDbType.Jsonb)
            {
                Value = responseJson
            });

        var affected =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        if (affected != 1)
        {
            throw new InvalidOperationException(
                "Failed to complete idempotency record.");
        }
    }
}

public sealed record IdempotencyReservation(
    IdempotencyReservationKind Kind,
    string? ResponseJson)
{
    public static IdempotencyReservation New()
        => new(
            IdempotencyReservationKind.New,
            null);

    public static IdempotencyReservation Replay(
        string responseJson)
        => new(
            IdempotencyReservationKind.Replay,
            responseJson);

    public static IdempotencyReservation Conflict()
        => new(
            IdempotencyReservationKind.Conflict,
            null);
}

public enum IdempotencyReservationKind
{
    New,
    Replay,
    Conflict
}