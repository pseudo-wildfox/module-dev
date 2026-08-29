using System.Net.Http.Headers;

namespace Api.Tests;

[Collection("PostgreSQL collection")]
public abstract class ApiTestBase : IAsyncLifetime
{
    protected PostgreSqlFixture Postgres { get; }

    protected TestApplicationFactory Factory { get; private set; } = null!;

    protected HttpClient Client { get; private set; } = null!;

    protected ApiTestBase(
        PostgreSqlFixture postgres)
    {
        Postgres = postgres;
    }

    public async Task InitializeAsync()
    {
        await Postgres.ResetStateAsync();

        Factory =
            new TestApplicationFactory(
                Postgres);

        Client =
            Factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();

        await Factory.DisposeAsync();
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