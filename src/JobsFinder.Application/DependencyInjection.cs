using FluentValidation;
using JobsFinder.Application.Jobs;
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

        return services;
    }
}
