using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PersonaOS.Application.Configuration;

namespace PersonaOS.Api.Infrastructure;

/// <summary>
/// Gates a controller or action behind an InstanceConfig feature toggle.
/// Disabled module → 403 with a machine-readable error, so clients that missed
/// the /api/branding feature list still fail cleanly.
/// Usage: [RequireFeature(InstanceConfig.Modules.Docs)]
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireFeatureAttribute(string module) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var configService = context.HttpContext.RequestServices.GetRequiredService<IInstanceConfigService>();
        var config = await configService.GetOrCreateAsync(context.HttpContext.RequestAborted);

        if (!config.Features.TryGetValue(module, out var enabled) || !enabled)
        {
            context.Result = new ObjectResult(new
            {
                error = $"The '{module}' feature is disabled on this instance.",
                code = "feature_disabled",
                feature = module,
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}
