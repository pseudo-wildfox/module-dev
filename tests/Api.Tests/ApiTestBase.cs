using System.Net.Http.Headers;
using Api.Tests.Helpers;

namespace Api.Tests;

[Collection("API collection")]
public abstract class ApiTestBase : IAsyncLifetime
{
    protected ApiTestFixture Fixture { get; }

    protected PostgreSqlFixture Postgres =>
        Fixture.Postgres;

    protected TestApplicationFactory Factory =>
        Fixture.Factory;

    protected HttpClient Client { get; private set; } = null!;

    protected AutocheckRepository Autocheck { get; private set; } = null!;

    protected ApiTestBase(
        ApiTestFixture fixture)
    {
        Fixture = fixture;
    }

    public Task InitializeAsync()
    {
        Client =
            Fixture.Factory.CreateClient();

        Autocheck =
            new AutocheckRepository(
                Postgres.ConnectionString);

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Client.Dispose();

        return Task.CompletedTask;
    }

    protected void SetBearerToken(
        string token)
    {
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);
    }
}