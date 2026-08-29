namespace Api.Tests;

[CollectionDefinition(
    "API collection",
    DisableParallelization = true)]
public sealed class ApiCollection
    : ICollectionFixture<ApiTestFixture>
{
}