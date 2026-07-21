using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PersonaOS.Application.Common.Exceptions;

namespace PersonaOS.Api.Infrastructure;

/// <summary>
/// Turns any <see cref="DomainValidationException"/> (bad input reaching a domain
/// service) into a 400 with a machine-readable body, instead of a 500. Every module
/// reuses this — subclass <c>DomainValidationException</c> rather than adding a filter.
/// </summary>
public class DomainValidationExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not DomainValidationException ex) return;

        context.Result = new ObjectResult(new
        {
            error = ex.Message,
            code = ex.Code,
        })
        { StatusCode = StatusCodes.Status400BadRequest };
        context.ExceptionHandled = true;
    }
}
