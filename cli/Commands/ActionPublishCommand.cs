using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModuleDev.Cli.Database;
using ModuleDev.Cli.Output;
using ModuleDev.Contracts;
using ModuleDev.Contracts.Validation;
using Npgsql;
using NpgsqlTypes;

namespace ModuleDev.Cli.Commands;

public sealed class ActionPublishCommand
{
    private readonly CliDatabase _database;
    private readonly ActionManifestValidator _validator;
    private readonly CliOutput _output;

    public ActionPublishCommand(
        CliDatabase database,
        ActionManifestValidator validator,
        CliOutput output)
    {
        _database = database;
        _validator = validator;
        _output = output;
    }

    public async Task<int> ExecuteAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(manifestPath))
        {
            return _output.Error(
                code: "request.invalid",
                message: "manifest file not found");
        }

        var manifestJson = await File.ReadAllTextAsync(
            manifestPath,
            cancellationToken);

        var validation = _validator.Validate(
            manifestJson);

        if (!validation.IsValid)
        {
            return _output.Error(
                code: "manifest.invalid",
                message: "manifest does not match schema",
                details: new
                {
                    errors = validation.Errors
                });
        }

        ActionManifest manifest;

        try
        {
            manifest = JsonSerializer.Deserialize<ActionManifest>(
                manifestJson,
                JsonOptions)
                ?? throw new InvalidOperationException(
                    "manifest deserialized to null");
        }
        catch (JsonException)
        {
            return _output.Error(
                code: "manifest.invalid",
                message: "manifest could not be deserialized");
        }

        var manifestHash = CalculateImmutableManifestHash(
            manifest);

        await using var connection =
            await _database.OpenPublisherConnectionAsync(
                cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        const string insertSql = """
            INSERT INTO runtime.action_definitions
            (
                contract_version,
                module,
                action,
                version,
                http_method,
                target_schema,
                target_function,
                request_schema,
                response_schema,
                outcomes,
                required_policy,
                idempotency_mode,
                idempotency_scope,
                timeout_ms,
                enabled,
                is_default,
                manifest_hash
            )
            VALUES
            (
                @contract_version,
                @module,
                @action,
                @version,
                @http_method,
                @target_schema,
                @target_function,
                @request_schema,
                @response_schema,
                @outcomes,
                @required_policy,
                @idempotency_mode,
                @idempotency_scope,
                @timeout_ms,
                @enabled,
                @is_default,
                @manifest_hash
            )
            ON CONFLICT (module, action, version)
            DO NOTHING
            RETURNING id;
            """;

        await using (var insertCommand =
            new NpgsqlCommand(insertSql, connection, transaction))
        {
            AddParameters(insertCommand, manifest, manifestHash);

            var insertedId =
                await insertCommand.ExecuteScalarAsync(
                    cancellationToken);

            if (insertedId is not null)
            {
                await transaction.CommitAsync(
                    cancellationToken);

                return _output.Success(new
                {
                    resource = "action",
                    operation = "published",
                    key = $"{manifest.Module}.{manifest.Action}",
                    version = manifest.Version
                });
            }
        }

        const string existingSql = """
            SELECT manifest_hash
            FROM runtime.action_definitions
            WHERE module = @module
              AND action = @action
              AND version = @version
            FOR UPDATE;
            """;

        await using var existingCommand =
            new NpgsqlCommand(
                existingSql,
                connection,
                transaction);

        existingCommand.Parameters.AddWithValue(
            "module",
            manifest.Module);

        existingCommand.Parameters.AddWithValue(
            "action",
            manifest.Action);

        existingCommand.Parameters.AddWithValue(
            "version",
            manifest.Version);

        var existingHash =
            await existingCommand.ExecuteScalarAsync(
                cancellationToken) as string;

        if (existingHash is null)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            return _output.Error(
                code: "internal.error",
                message: "published action version could not be resolved");
        }

        if (string.Equals(
                existingHash,
                manifestHash,
                StringComparison.Ordinal))
        {
            await transaction.CommitAsync(
                cancellationToken);

            return _output.Success(new
            {
                resource = "action",
                operation = "published",
                key = $"{manifest.Module}.{manifest.Action}",
                version = manifest.Version
            });
        }

        await transaction.RollbackAsync(
            cancellationToken);

        return _output.Error(
            code: "manifest.conflict",
            message: "published action version is immutable");
    }

    private static void AddParameters(
        NpgsqlCommand command,
        ActionManifest manifest,
        string manifestHash)
    {
        command.Parameters.AddWithValue(
            "contract_version",
            manifest.ContractVersion);

        command.Parameters.AddWithValue(
            "module",
            manifest.Module);

        command.Parameters.AddWithValue(
            "action",
            manifest.Action);

        command.Parameters.AddWithValue(
            "version",
            manifest.Version);

        command.Parameters.AddWithValue(
            "http_method",
            manifest.HttpMethod);

        command.Parameters.AddWithValue(
            "target_schema",
            manifest.TargetSchema);

        command.Parameters.AddWithValue(
            "target_function",
            manifest.TargetFunction);

        AddJsonbParameter(
            command,
            "request_schema",
            manifest.RequestSchema);

        AddJsonbParameter(
            command,
            "response_schema",
            manifest.ResponseSchema);

        AddJsonbParameter(
            command,
            "outcomes",
            manifest.Outcomes);

        AddJsonbParameter(
            command,
            "required_policy",
            manifest.RequiredPolicy);

        command.Parameters.AddWithValue(
            "idempotency_mode",
            manifest.IdempotencyMode);

        command.Parameters.AddWithValue(
            "idempotency_scope",
            manifest.IdempotencyScope);

        command.Parameters.AddWithValue(
            "timeout_ms",
            manifest.TimeoutMs);

        command.Parameters.AddWithValue(
            "enabled",
            manifest.Enabled);

        command.Parameters.AddWithValue(
            "is_default",
            manifest.IsDefault);

        command.Parameters.AddWithValue(
            "manifest_hash",
            manifestHash);
    }

    private static void AddJsonbParameter(
        NpgsqlCommand command,
        string name,
        object value)
    {
        var parameter = command.Parameters.Add(
            name,
            NpgsqlDbType.Jsonb);

        parameter.Value = JsonSerializer.Serialize(
            value,
            JsonOptions);
    }

    private static string CalculateImmutableManifestHash(
        ActionManifest manifest)
    {
        var immutableManifest = new
        {
            contract_version = manifest.ContractVersion,
            module = manifest.Module,
            action = manifest.Action,
            version = manifest.Version,
            http_method = manifest.HttpMethod,
            target_schema = manifest.TargetSchema,
            target_function = manifest.TargetFunction,
            request_schema = manifest.RequestSchema,
            response_schema = manifest.ResponseSchema,
            outcomes = manifest.Outcomes,
            required_policy = manifest.RequiredPolicy,
            idempotency_mode = manifest.IdempotencyMode,
            idempotency_scope = manifest.IdempotencyScope,
            timeout_ms = manifest.TimeoutMs
        };

        var json = JsonSerializer.Serialize(
            immutableManifest,
            JsonOptions);

        var bytes = Encoding.UTF8.GetBytes(json);

        return Convert.ToHexString(
                SHA256.HashData(bytes))
            .ToLowerInvariant();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}