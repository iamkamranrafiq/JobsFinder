using JobsFinder.Application.Abstractions;
using JobsFinder.Application.Jobs;
using JobsFinder.Infrastructure.Persistence;
using JobsFinder.Infrastructure.Persistence.Repositories;
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

        return services;
    }
}
