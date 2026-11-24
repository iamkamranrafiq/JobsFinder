using JobsFinder.Application.Abstractions;
using JobsFinder.Application.Cv;
using JobsFinder.Application.Jobs;
using JobsFinder.Application.Sourcing;
using JobsFinder.Infrastructure.Ai;
using JobsFinder.Infrastructure.Cv;
using JobsFinder.Infrastructure.Persistence;
using JobsFinder.Infrastructure.Persistence.Repositories;
using JobsFinder.Infrastructure.Sourcing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobsFinder.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("JobsFinder")
            ?? throw new InvalidOperationException("Connection string 'JobsFinder' is missing.");

        // No lazy loading proxies: load related data explicitly with Include or projections.
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<ICvRepository, CvRepository>();
        services.AddScoped<IJobPostingRepository, JobPostingRepository>();
        services.AddScoped<IFetchRunRepository, FetchRunRepository>();
        services.AddSingleton<ICvTextExtractor, CvTextExtractor>();

        services.AddAIProvider(configuration);
        services.AddJobSource(configuration);

        return services;
    }

    private static void AddJobSource(this IServiceCollection services, IConfiguration configuration)
    {
        // A missing token or actor must not stop the app: the fetch itself fails as "Source unavailable" (F0.2-AC4).
        var options = configuration.GetSection(ApifyOptions.SectionName).Get<ApifyOptions>() ?? new ApifyOptions();
        services.AddSingleton(options);

        services.AddHttpClient<IJobSource, ApifyJobSource>(client =>
            {
                client.BaseAddress = options.BaseUrl;
                client.Timeout = TimeSpan.FromSeconds(60);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false });

        // F0.2-AC1: one instance is both the runner the handlers see and the hosted service that runs the fetch.
        services.AddSingleton<FetchWorker>();
        services.AddSingleton<IFetchRunner>(sp => sp.GetRequiredService<FetchWorker>());
        services.AddHostedService(sp => sp.GetRequiredService<FetchWorker>());
    }

    private static void AddAIProvider(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AIOptions.SectionName).Get<AIOptions>() ?? new AIOptions();
        services.AddSingleton(options);

        switch (options.ResolveProvider())
        {
            case AIProviderKind.Anthropic:
                if (string.IsNullOrWhiteSpace(options.Anthropic.ApiKey))
                {
                    throw new InvalidOperationException(
                        "AI:Provider is 'Anthropic' but AI:Anthropic:ApiKey is not set. Set it with user-secrets or an environment variable.");
                }

                services.AddHttpClient<IAIProvider, AnthropicAIProvider>(client =>
                {
                    client.BaseAddress = options.Anthropic.BaseUrl;
                    client.Timeout = TimeSpan.FromSeconds(options.Anthropic.TimeoutSeconds);
                });
                break;

            default:
                services.AddSingleton<IAIProvider, StubAIProvider>();
                break;
        }
    }
}
