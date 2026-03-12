using Aco228.Common.Extensions;
using Aco228.Common.Infrastructure;
using Aco228.Common.LocalStorage;
using Aco228.ContextualImage.Infrastructure;
using Aco228.ContextualImage.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Aco228.ContextualImage;

public static class ServiceExtensions
{
    public static void RegisterContextualImageServices(this IServiceCollection services)
        => typeof(ServiceExtensions).RegisterIfNot(() =>
        {
            services.AddTransient<ContextualImageFlowPrimaryAndSecondaryService>();
            services.AddTransient<ContextualImageFlowPrimaryService>();
            services.AddTransient<FlowPrimaryTextBlurService>();
            services.AddTransient<FlowPrimaryTextService>();
            services.RegisterPostBuildAction((p) =>
            {
                var fontsFolder = StorageManager.Instance.GetFolder("Fonts");
                FontManager.LoadFonts(fontsFolder);
            });
        });
}