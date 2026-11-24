using JobsFinder.Application.Abstractions;
using JobsFinder.Application.Sourcing;
using JobsFinder.IntegrationTests.Cv;
using JobsFinder.IntegrationTests.Sourcing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace JobsFinder.IntegrationTests;

/// <summary>
/// Boots the real API against a throwaway PostgreSQL container (Docker must be running).
/// Runs in Development so the API applies migrations on startup.
/// The AI provider and job source are replaced by scripted fakes: tests never call a real model or Apify.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Scripted model answers for the current test (shared; tests in the collection run sequentially).</summary>
    public ScriptedAIProvider AI { get; } = new();

    /// <summary>Scripted job source for the current test (shared; tests in the collection run sequentially).</summary>
    public ScriptedJobSource JobSource { get; } = new();

    public async Task InitializeAsync() => await _db.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _db.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:JobsFinder", _db.GetConnectionString());
        // Never pick up a developer's real API key or token from user-secrets or env vars.
        builder.UseSetting("AI:Provider", "Stub");
        builder.UseSetting("Apify:Token", string.Empty);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAIProvider>();
            services.AddSingleton<IAIProvider>(AI);
            services.RemoveAll<IJobSource>();
            services.AddSingleton<IJobSource>(JobSource);
        });
    }
}

[CollectionDefinition(Name)]
public sealed class ApiTestGroup : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
