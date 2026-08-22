using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Infrastructure.Ai;

/// <summary>
/// Picks the streaming adapter for the configured provider. Unknown values fall
/// back to Anthropic so a misconfigured install degrades to the default rather
/// than failing to resolve a service.
/// </summary>
public class AiMessageStreamerFactory(
    AnthropicMessageStreamer anthropic,
    OpenAiCompatibleMessageStreamer openAiCompatible) : IAiMessageStreamerFactory
{
    public IAiMessageStreamer ForProvider(string provider) => provider switch
    {
        InstanceConfig.Providers.OpenAiCompatible => openAiCompatible,
        _ => anthropic,
    };
}
