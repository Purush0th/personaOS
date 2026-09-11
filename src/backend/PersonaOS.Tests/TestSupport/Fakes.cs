using PersonaOS.Application.Ai;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using Microsoft.EntityFrameworkCore;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Tests.TestSupport;

/// <summary>Scriptable push sender: queue the outcome each call should produce.</summary>
public class FakePushSender : IPushSender
{
    public bool IsConfigured { get; set; } = true;

    /// <summary>Tokens the provider should report as permanently invalid.</summary>
    public HashSet<string> InvalidTokens { get; } = new();

    /// <summary>When false, every send fails with <see cref="FailureReason"/>.</summary>
    public bool Succeeds { get; set; } = true;

    public string FailureReason { get; set; } = "provider unavailable";

    public int SendCount { get; private set; }
    public List<string> SentBodies { get; } = new();

    public Task<IReadOnlyList<PushResult>> SendAsync(
        IReadOnlyList<string> tokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken ct = default)
    {
        SendCount++;
        SentBodies.Add(body);

        IReadOnlyList<PushResult> results = tokens.Select(t =>
            InvalidTokens.Contains(t)
                ? new PushResult(t, Success: false, TokenInvalid: true, Error: "unregistered token")
                : Succeeds
                    ? new PushResult(t, Success: true)
                    : new PushResult(t, Success: false, Error: FailureReason))
            .ToList();

        return Task.FromResult(results);
    }
}

/// <summary>
/// Scriptable AI streamer: each queued round is the chunk sequence one model
/// call should emit, letting tests drive the tool loop deterministically.
/// </summary>
public class FakeAiMessageStreamer : IAiMessageStreamer
{
    private readonly Queue<IReadOnlyList<AiStreamChunk>> _rounds = new();

    /// <summary>Turns passed in on each call, so tests can assert what the model saw.</summary>
    public List<IReadOnlyList<AiChatTurn>> ReceivedTurns { get; } = new();

    public List<IReadOnlyList<AiToolDefinition>> ReceivedTools { get; } = new();

    public Exception? ThrowOnFirstCall { get; set; }

    public int CallCount { get; private set; }

    public FakeAiMessageStreamer EnqueueText(string text, long inputTokens = 10, long outputTokens = 5) =>
        Enqueue([
            new AiStreamChunk(InputTokens: inputTokens),
            new AiStreamChunk(TextDelta: text),
            new AiStreamChunk(OutputTokens: outputTokens),
        ]);

    public FakeAiMessageStreamer EnqueueToolCall(string id, string name, string inputJson, string? text = null)
    {
        var chunks = new List<AiStreamChunk> { new(InputTokens: 10) };
        if (text is not null) chunks.Add(new AiStreamChunk(TextDelta: text));
        chunks.Add(new AiStreamChunk(ToolCall: new AiToolCall(id, name, inputJson)));
        chunks.Add(new AiStreamChunk(OutputTokens: 5, StopReason: AiStopReasons.ToolUse));
        return Enqueue(chunks);
    }

    public FakeAiMessageStreamer Enqueue(IReadOnlyList<AiStreamChunk> chunks)
    {
        _rounds.Enqueue(chunks);
        return this;
    }

    public async IAsyncEnumerable<AiStreamChunk> StreamAsync(
        string apiKey,
        string model,
        string? baseUrl,
        string systemPrompt,
        IReadOnlyList<AiChatTurn> turns,
        IReadOnlyList<AiToolDefinition> tools,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        CallCount++;
        // Snapshot: ChatService mutates its list between rounds.
        ReceivedTurns.Add(turns.ToList());
        ReceivedTools.Add(tools.ToList());

        if (ThrowOnFirstCall is not null && CallCount == 1)
        {
            throw ThrowOnFirstCall;
        }

        var chunks = _rounds.Count > 0
            ? _rounds.Dequeue()
            : [new AiStreamChunk(TextDelta: "done")];

        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();
            yield return chunk;
            await Task.Yield();
        }
    }
}

/// <summary>Factory that always returns the one streamer it wraps, regardless of provider.</summary>
public class FakeAiMessageStreamerFactory(IAiMessageStreamer streamer) : IAiMessageStreamerFactory
{
    public IAiMessageStreamer ForProvider(string provider) => streamer;
}

/// <summary>Returns a fixed system prompt so prompt content doesn't couple chat tests.</summary>
public class FakeSystemPromptBuilder(string prompt = "You are a test assistant.") : ISystemPromptBuilder
{
    public Task<string> BuildAsync(CancellationToken ct = default) => Task.FromResult(prompt);
}

/// <summary>Minimal config service over the in-memory context.</summary>
public class FakeInstanceConfigService(TestDbContext db, string? apiKey = "sk-ant-test") : IInstanceConfigService
{
    public async Task<InstanceConfig> GetOrCreateAsync(CancellationToken ct = default)
    {
        var config = await db.InstanceConfig.FirstOrDefaultAsync(ct);
        if (config is not null) return config;

        config = new InstanceConfig
        {
            Id = InstanceConfig.SingletonId,
            IsConfigured = true,
            AssistantNickname = "Friday",
            TimeZone = "UTC",
            AiModel = "claude-opus-4-8",
            Features = InstanceConfig.DefaultFeatures(),
        };
        db.InstanceConfig.Add(config);
        await db.SaveChangesAsync(ct);
        return config;
    }

    public async Task UpdateAsync(Action<InstanceConfig> apply, CancellationToken ct = default)
    {
        var config = await GetOrCreateAsync(ct);
        apply(config);
        await db.SaveChangesAsync(ct);
    }

    public Task SetAnthropicApiKeyAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

    public Task<string?> GetAnthropicApiKeyAsync(CancellationToken ct = default) => Task.FromResult(apiKey);
}

/// <summary>A tool that records its invocations and returns a canned result.</summary>
public class FakeTool(
    string name,
    string result = "{\"ok\":true}",
    string? requiredFeature = null,
    bool mutates = false) : PersonaOS.Application.Ai.Tools.IPersonaTool
{
    public string Name { get; } = name;
    public string Description => $"Fake {Name} tool.";
    public string InputSchemaJson => """{"type":"object","properties":{}}""";
    public string? RequiredFeature { get; } = requiredFeature;

    /// <summary>
    /// Defaults to false so a test that just wants a tool to run says so by saying nothing.
    /// Pass true to exercise the confirmation gate.
    /// </summary>
    public bool Mutates { get; } = mutates;

    public List<string> Invocations { get; } = new();

    public Task<string> ExecuteAsync(System.Text.Json.JsonElement input, CancellationToken ct = default)
    {
        Invocations.Add(input.ToString());
        return Task.FromResult(result);
    }
}

/// <summary>Clock frozen at a fixed instant so scheduling tests never depend on wall time.</summary>
public class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Reversible stand-in for the real hasher: "hash" is just a prefixed copy of the
/// password, so tests can assert on rehashing without running a KDF. Set
/// <see cref="NextVerifyResult"/> to script the outcome of the next Verify call.
/// </summary>
public class FakePasswordHasher : IPasswordHasher
{
    private const string Prefix = "hashed:";

    public PasswordVerifyResult? NextVerifyResult { get; set; }
    public List<string> Hashed { get; } = new();

    public string Hash(string password)
    {
        Hashed.Add(password);
        return Prefix + password;
    }

    public PasswordVerifyResult Verify(string hash, string password)
    {
        if (NextVerifyResult is { } scripted) return scripted;
        return hash == Prefix + password ? PasswordVerifyResult.Success : PasswordVerifyResult.Failed;
    }
}

/// <summary>Token generator that records who it was asked to issue for.</summary>
public class FakeJwtTokenGenerator : IJwtTokenGenerator
{
    public List<AdminUser> Issued { get; } = new();

    public TokenResult CreateToken(AdminUser admin)
    {
        Issued.Add(admin);
        return new TokenResult($"token-for-{admin.Username}", DateTime.UtcNow.AddHours(12));
    }
}
