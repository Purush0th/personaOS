using Microsoft.Extensions.DependencyInjection;
using PersonaOS.Application.Ai;
using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Ai.Prompts;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Application.Auth;
using PersonaOS.Application.Board;
using PersonaOS.Application.Board.Tools;
using PersonaOS.Application.Configuration;
using PersonaOS.Application.Documents;
using PersonaOS.Application.Documents.Tools;
using PersonaOS.Application.Goals;
using PersonaOS.Application.Goals.Tools;
using PersonaOS.Application.Planner;
using PersonaOS.Application.Proactive;
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
        services.AddSingleton<IPromptLibrary, PromptLibrary>();
        services.AddScoped<ISystemPromptBuilder, SystemPromptBuilder>();
        services.AddScoped<ToolCallPipeline>();
        services.AddScoped<ReplyPipeline>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IGoalService, GoalService>();
        services.AddScoped<IBoardService, BoardService>();
        services.AddScoped<PersonaOS.Application.WorkItems.IWorkItemService, PersonaOS.Application.WorkItems.WorkItemService>();
        services.AddScoped<IPlannerService, PlannerService>();
        services.AddScoped<IReminderService, ReminderService>();
        services.AddScoped<IReminderAlarmPublisher, ReminderAlarmPublisher>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<PersonaOS.Application.Push.IPushConfigService, PersonaOS.Application.Push.PushConfigService>();
        services.AddScoped<IReminderDispatcher, ReminderDispatcher>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IProactiveBriefComposer, ProactiveBriefComposer>();
        services.AddScoped<IProactiveService, ProactiveService>();

        services.AddScoped<IPersonaToolRegistry, PersonaToolRegistry>();
        services.AddScoped<IPersonaTool, GetGoalsTool>();
        services.AddScoped<IPersonaTool, CreateGoalTool>();
        services.AddScoped<IPersonaTool, UpdateGoalStatusTool>();
        services.AddScoped<IPersonaTool, DeleteGoalTool>();
        services.AddScoped<IPersonaTool, GetBoardTool>();
        services.AddScoped<IPersonaTool, GetPlanTool>();
        services.AddScoped<IPersonaTool, CreateSprintTool>();
        services.AddScoped<IPersonaTool, StartSprintTool>();
        services.AddScoped<IPersonaTool, CompleteSprintTool>();
        services.AddScoped<IPersonaTool, AddCommentTool>();
        services.AddScoped<IPersonaTool, GetSprintReportTool>();
        services.AddScoped<IPersonaTool, CreateTaskTool>();
        services.AddScoped<IPersonaTool, UpdateTaskTool>();
        services.AddScoped<IPersonaTool, MoveTaskTool>();
        services.AddScoped<IPersonaTool, DeleteTaskTool>();
        services.AddScoped<IPersonaTool, GetPlannerTool>();
        services.AddScoped<IPersonaTool, AddPlannerItemTool>();
        services.AddScoped<IPersonaTool, UpdatePlannerItemStatusTool>();
        services.AddScoped<IPersonaTool, MovePlannerItemTool>();
        services.AddScoped<IPersonaTool, GetRemindersTool>();
        services.AddScoped<IPersonaTool, CreateReminderTool>();
        services.AddScoped<IPersonaTool, CancelReminderTool>();
        services.AddScoped<IPersonaTool, ListDocumentsTool>();
        services.AddScoped<IPersonaTool, ReadDocumentTool>();

        return services;
    }
}
