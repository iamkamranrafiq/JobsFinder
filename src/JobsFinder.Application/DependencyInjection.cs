using FluentValidation;
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
        services.AddScoped<GetSourceStatusHandler>();

        return services;
    }
}
