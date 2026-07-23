using Microsoft.Extensions.DependencyInjection;
using PersonaOS.Application.Ai;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Application.Auth;
using PersonaOS.Application.Configuration;
using PersonaOS.Application.Goals;
using PersonaOS.Application.Goals.Tools;
using PersonaOS.Application.Planner;
using PersonaOS.Application.Planner.Tools;
using PersonaOS.Application.Reminders;
using PersonaOS.Application.Reminders.Tools;

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
        services.AddScoped<IGoalService, GoalService>();
        services.AddScoped<IPlannerService, PlannerService>();
        services.AddScoped<IReminderService, ReminderService>();
        services.AddScoped<IReminderDispatcher, ReminderDispatcher>();

        services.AddScoped<IPersonaToolRegistry, PersonaToolRegistry>();
        services.AddScoped<IPersonaTool, GetGoalsTool>();
        services.AddScoped<IPersonaTool, CreateGoalTool>();
        services.AddScoped<IPersonaTool, UpdateGoalStatusTool>();
        services.AddScoped<IPersonaTool, LinkGoalTool>();
        services.AddScoped<IPersonaTool, GetPlannerTool>();
        services.AddScoped<IPersonaTool, AddPlannerItemTool>();
        services.AddScoped<IPersonaTool, UpdatePlannerItemStatusTool>();
        services.AddScoped<IPersonaTool, MovePlannerItemTool>();
        services.AddScoped<IPersonaTool, GetRemindersTool>();
        services.AddScoped<IPersonaTool, CreateReminderTool>();
        services.AddScoped<IPersonaTool, CancelReminderTool>();

        return services;
    }
}
