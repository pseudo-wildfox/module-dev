namespace Api.Tests;

[CollectionDefinition(
    "PostgreSQL collection",
    DisableParallelization = true)]
public sealed class PostgreSqlCollection
    : ICollectionFixture<PostgreSqlFixture>
{
}