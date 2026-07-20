using Microsoft.Extensions.DependencyInjection;
using PersonaOS.Application.Ai;
using PersonaOS.Application.Auth;
using PersonaOS.Application.Configuration;

namespace PersonaOS.Application;

public static class DependencyInjection
{
    /// <summary>Registers all Application-layer use cases. Ports are bound in Infrastructure.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IInstanceConfigService, InstanceConfigService>();
        services.AddScoped<ISystemPromptBuilder, SystemPromptBuilder>();
        services.AddScoped<IChatService, ChatService>();
        return services;
    }
}
