namespace Api.Tests;

public sealed class ApiTestFixture : IAsyncLifetime
{
    public PostgreSqlFixture Postgres { get; } =
        new();

    public TestApplicationFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Postgres.InitializeAsync();

        Factory =
            new TestApplicationFactory(Postgres);
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        await Postgres.DisposeAsync();
    }
}