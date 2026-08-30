namespace TransLink.Lite.IntegrationTests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationTestCollection : ICollectionFixture<PostgreSqlApiFixture>
{
    public const string Name = "PostgreSQL integration";
}
