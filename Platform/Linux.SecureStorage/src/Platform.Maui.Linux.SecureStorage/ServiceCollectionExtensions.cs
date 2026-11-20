using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Storage;

namespace Platform.Maui.Linux.SecureStorage;

/// <summary>Registers Linux Secret Service storage with dependency injection.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Replaces existing ISecureStorage registrations with an application-scoped singleton.</summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="applicationId">A stable, unique application identifier.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddLinuxSecureStorage(this IServiceCollection services, string applicationId)
    {
        ArgumentNullException.ThrowIfNull(services);
        LinuxSecureStorage.ValidateText(applicationId, nameof(applicationId), allowEmpty: false);
        services.RemoveAll<ISecureStorage>();
        services.AddSingleton<ISecureStorage>(_ => new LinuxSecureStorage(applicationId));
        return services;
    }
}
