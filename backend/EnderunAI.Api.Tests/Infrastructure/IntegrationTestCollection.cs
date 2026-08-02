using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests.Infrastructure;

public sealed class DatabaseFixture : IAsyncLifetime
{
    public TestWebApplicationFactory Factory { get; } = new();

    public async Task InitializeAsync()
    {
        await TestWebApplicationFactory.DropTestDatabaseAsync();

        // İlk Services erişimi host'u build eder; Program.cs'in kendi
        // MigrationRecovery mantığı veritabanını oluşturup migrate edip
        // admin kullanıcıyı seed eder.
        using var scope = Factory.Services.CreateScope();
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
    }
}

[CollectionDefinition("Integration", DisableParallelization = true)]
public sealed class IntegrationTestCollection : ICollectionFixture<DatabaseFixture>;
