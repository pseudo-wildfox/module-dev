using System.Text.Json;
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

    public ActionExecutor(
        DbConnectionFactory connectionFactory,
        ActionResponseValidator responseValidator)
    {
        _connectionFactory = connectionFactory;
        _responseValidator = responseValidator;
    }

    public async Task<string> ExecuteAsync(
        ActionManifest manifest,
        TrustedContext context,
        JsonDocument payload,
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

        try
        {

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
                new NpgsqlParameter("module", NpgsqlDbType.Text)
                {
                    Value = manifest.Module
                });

            command.Parameters.Add(
                new NpgsqlParameter("action", NpgsqlDbType.Text)
                {
                    Value = manifest.Action
                });

            command.Parameters.Add(
                new NpgsqlParameter("version", NpgsqlDbType.Integer)
                {
                    Value = manifest.Version
                });

            command.Parameters.Add(
                new NpgsqlParameter("context", NpgsqlDbType.Jsonb)
                {
                    Value = contextJson
                });

            command.Parameters.Add(
                new NpgsqlParameter("payload", NpgsqlDbType.Jsonb)
                {
                    Value = payload
                });


            var value =
                await command.ExecuteScalarAsync(
                    cancellationToken);

            if (value is not string responseJson)
            {
                responseJson = value?.ToString()
                    ?? throw new InvalidOperationException(
                        "api.invoke returned NULL.");
            }

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

            var result =
                response.RootElement.Clone();

            await transaction.CommitAsync(
                cancellationToken);

            return result.GetRawText();
        }
        catch
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            throw;
        }
    }
}
