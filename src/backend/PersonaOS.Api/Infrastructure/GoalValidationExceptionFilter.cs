using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PersonaOS.Application.Goals;

namespace PersonaOS.Api.Infrastructure;

/// <summary>
/// Turns <see cref="GoalValidationException"/> (bad input reaching a goal service)
/// into a 400 with a machine-readable body, instead of a 500.
/// </summary>
public class GoalValidationExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not GoalValidationException ex) return;

        context.Result = new ObjectResult(new
        {
            error = ex.Message,
            code = "goal_validation_failed",
        })
        { StatusCode = StatusCodes.Status400BadRequest };
        context.ExceptionHandled = true;
    }
}
