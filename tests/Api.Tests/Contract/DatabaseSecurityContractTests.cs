using System.Text.Json;
using System.Net;
using System.Net.Http.Headers;
using Api.Tests.Helpers;
using Npgsql;

namespace Api.Tests.Contract;

public sealed class DatabaseSecurityContractTests
    : ApiTestBase
{
    public DatabaseSecurityContractTests(
        ApiTestFixture fixture)
        : base(fixture)
    {
    }


    [Fact]
    public async Task TargetContractViolation_RollsBackTargetSideEffect()
    {
        var module =
            $"rollback{Guid.NewGuid():N}"[..20];

        var action =
            "probe";

        var manifestPath =
            Cli.CreateRollbackManifestFile(
                module,
                action);

        var marker =
            $"marker-{Guid.NewGuid():N}";

        var requestId =
            $"request-{Guid.NewGuid():N}";

        try
        {
            var publish =
                await Cli.PublishAsync(
                    manifestPath);

            Assert.Equal(
                0,
                publish.ExitCode);

            var activate =
                await Cli.ActivateAsync(
                    $"{module}.{action}",
                    1);

            Assert.Equal(
                0,
                activate.ExitCode);

            var beforeCanary =
                await Autocheck.CountRollbackCanaryAsync(
                    marker);

            Assert.Equal(
                0,
                beforeCanary);

            var beforeDispatches =
                await Autocheck.CountDispatchesAsync(
                    module,
                    action,
                    requestId);

            Assert.Equal(
                0,
                beforeDispatches);

            using var request =
                ActionHttpHelper.CreateGenericRequest(
                    $"/api/{module}/{action}",
                    new
                    {
                        marker
                    },
                    TestJwt.CreateWorker(),
                    requestId);

            using var response =
                await Client.SendAsync(request);

            using var document =
                await ActionHttpHelper.ReadJsonAsync(
                    response);

            ActionHttpHelper.AssertError(
                response,
                document.RootElement,
                HttpStatusCode.InternalServerError,
                "action.contract_violation");

            var afterCanary =
                await Autocheck.CountRollbackCanaryAsync(
                    marker);

            Assert.Equal(
                0,
                afterCanary);

            var afterDispatches =
                await Autocheck.CountDispatchesAsync(
                    module,
                    action,
                    requestId);

            Assert.Equal(
                0,
                afterDispatches);
        }
        finally
        {
            Cli.DeleteManifestFile(
                manifestPath);
        }
    }


    [Fact]
    public async Task ApiInvoke_WithoutRequiredPolicy_ReturnsAccessDenied()
    {
        const string module = "payment";
        const string action = "request";
        const int version = 1;

        var correlationId =
            Guid.NewGuid();

        var payload =
            """
            {
                "operationKind": "PAYMENT_EXECUTION",
                "amount": "100.00",
                "currency": "RUB"
            }
            """;

        var context =
            $$"""
            {
                "principal": "denied-client",
                "consumer": "test",
                "scopes": [],
                "correlationId": "{{correlationId}}",
                "requestId": null,
                "deadline": "2099-01-01T00:00:00Z"
            }
            """;

        await using var connection =
            new NpgsqlConnection(
                Postgres.ConnectionString);

        await connection.OpenAsync();

        await using var roleCommand =
            new NpgsqlCommand(
                "SET ROLE course_runtime;",
                connection);

        await roleCommand.ExecuteNonQueryAsync();

        const string sql = """
            SELECT api.invoke(
                @module,
                @action,
                @version,
                CAST(@context AS jsonb),
                CAST(@payload AS jsonb)
            );
            """;

        await using var command =
            new NpgsqlCommand(
                sql,
                connection);

        command.Parameters.AddWithValue(
            "module",
            module);

        command.Parameters.AddWithValue(
            "action",
            action);

        command.Parameters.AddWithValue(
            "version",
            version);

        command.Parameters.AddWithValue(
            "context",
            context);

        command.Parameters.AddWithValue(
            "payload",
            payload);

        var value =
            await command.ExecuteScalarAsync();

        Assert.NotNull(value);

        using var document =
            JsonDocument.Parse(
                value!.ToString()!);

        var body =
            document.RootElement;

        Assert.Equal(
            "error",
            body.GetProperty("status")
                .GetString());

        Assert.Equal(
            "access.denied",
            body.GetProperty("code")
                .GetString());
    }


    [Fact]
    public async Task RuntimeRole_CannotDirectlyMutatePaymentOperations()
    {
        await using var connection =
            new NpgsqlConnection(
                Postgres.ConnectionString);

        await connection.OpenAsync();

        await using var roleCommand =
            new NpgsqlCommand(
                "SET ROLE course_runtime;",
                connection);

        await roleCommand.ExecuteNonQueryAsync();


        var operationId =
            Guid.NewGuid();

        var insertSql =
            """
            INSERT INTO payment.operations
            (
                operation_id,
                request_id,
                operation_kind,
                amount,
                currency,
                status,
                process_id
            )
            VALUES
            (
                @operation_id,
                @request_id,
                'PAYMENT_EXECUTION',
                100.00,
                'RUB',
                'CREATED',
                NULL
            );
            """;

        await AssertInsufficientPrivilegeAsync(
            connection,
            insertSql,
            new NpgsqlParameter(
                "operation_id",
                NpgsqlTypes.NpgsqlDbType.Uuid)
            {
                Value = operationId
            },
            new NpgsqlParameter(
                "request_id",
                NpgsqlTypes.NpgsqlDbType.Text)
            {
                Value = $"direct-{Guid.NewGuid():N}"
            });


        var updateSql =
            """
            UPDATE payment.operations
            SET status = 'COMPLETED'
            WHERE operation_id = @operation_id;
            """;

        await AssertInsufficientPrivilegeAsync(
            connection,
            updateSql,
            new NpgsqlParameter(
                "operation_id",
                NpgsqlTypes.NpgsqlDbType.Uuid)
            {
                Value = Guid.NewGuid()
            });


        var deleteSql =
            """
            DELETE FROM payment.operations
            WHERE operation_id = @operation_id;
            """;

        await AssertInsufficientPrivilegeAsync(
            connection,
            deleteSql,
            new NpgsqlParameter(
                "operation_id",
                NpgsqlTypes.NpgsqlDbType.Uuid)
            {
                Value = Guid.NewGuid()
            });
    }


    private static async Task AssertInsufficientPrivilegeAsync(
        NpgsqlConnection connection,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var command =
            new NpgsqlCommand(
                sql,
                connection);

        command.Parameters.AddRange(
            parameters);

        var exception =
            await Assert.ThrowsAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync());

        Assert.Equal(
            "42501",
            exception.SqlState);
    }
}