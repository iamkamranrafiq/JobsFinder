using JobsFinder.Application.Applying;
using Microsoft.Extensions.DependencyInjection;

namespace JobsFinder.BrowserAutomation;

public static class DependencyInjection
{
    public static IServiceCollection AddBrowserAutomation(this IServiceCollection services) =>
        services.AddBrowserAutomation(new BrowserSettings());

    internal static IServiceCollection AddBrowserAutomation(this IServiceCollection services, BrowserSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton<IBrowserAutomationService, PlaywrightAutomationService>();

        return services;
    }
}
