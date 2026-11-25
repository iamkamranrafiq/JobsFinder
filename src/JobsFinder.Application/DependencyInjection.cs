using FluentValidation;
using JobsFinder.Application.Applying;
using JobsFinder.Application.Cv;
using JobsFinder.Application.Jobs;
using JobsFinder.Application.Sourcing;
using Microsoft.Extensions.DependencyInjection;

namespace JobsFinder.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        // One handler class per use case; register new ones here.
        services.AddScoped<CreateJobHandler>();
        services.AddScoped<GetJobHandler>();
        services.AddScoped<SearchJobsHandler>();

        services.AddSingleton(new CvParsingSettings());
        services.AddScoped<UploadCvHandler>();
        services.AddScoped<GetCvHandler>();
        services.AddScoped<DownloadCvFileHandler>();
        services.AddScoped<SaveProfileVersionHandler>();
        services.AddScoped<GetActiveCvHandler>();

        services.AddScoped<FetchJobPostingsHandler>();
        services.AddScoped<StartFetchHandler>();
        services.AddScoped<GetSourceStatusHandler>();
        services.AddScoped<SearchPostingsHandler>();

        return services;
    }

    /// <summary>
    /// The DryRun applier (F0.3). Separate from <see cref="AddApplication"/> because it needs an
    /// <see cref="IBrowserAutomationService"/>, which only a host with a browser registers; the API has none.
    /// </summary>
    public static IServiceCollection AddApplyPreparation(this IServiceCollection services)
    {
        services.AddSingleton(new ApplySettings());
        services.AddScoped<PrepareApplicationHandler>();

        return services;
    }
}
