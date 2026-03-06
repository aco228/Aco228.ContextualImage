using Aco228.Common.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Aco228.ContextualImage;

public static class ServiceExtensions
{
    public static void RegisterContextualImageServices(this IServiceCollection services)
        => typeof(ServiceExtensions).RegisterIfNot(() =>
        {
            
        });
}