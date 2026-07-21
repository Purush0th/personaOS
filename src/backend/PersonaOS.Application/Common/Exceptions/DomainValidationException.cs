namespace PersonaOS.Application.Common.Exceptions;

/// <summary>
/// Invalid input reaching a domain service. The message is user- and model-presentable;
/// <see cref="Code"/> is the machine-readable error code returned to API clients.
/// Modules subclass this so one API filter can turn them all into 400s.
/// </summary>
public abstract class DomainValidationException(string message, string code) : Exception(message)
{
    public string Code { get; } = code;
}
