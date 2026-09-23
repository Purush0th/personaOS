using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using PersonaOS.Application.Ai;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Ai;

/// <summary>
/// The chat loop following the model's profile: what it sends the provider, how long it waits for
/// a silent model, how many tool rounds it allows, and how it keeps a long conversation within
/// the model's context.
/// </summary>
public class ChatServiceModelProfileTests
{
    private static async Task<(TestDbContext Db, FakeInstanceConfigService Config, FakeAiMessageStreamer Streamer)> Setup(
        string provider, string model, int? contextTokens = null)
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        await config.UpdateAsync(c =>
        {
            c.AiProvider = provider;
            c.AiModel = model;
            c.AiBaseUrl = "http://localhost:11434";
            c.AiContextTokens = contextTokens;
        });
        return (db, config, new FakeAiMessageStreamer());
    }

    private static async Task<List<ChatStreamEvent>> CollectAsync(IAsyncEnumerable<ChatStreamEvent> stream)
    {
        var events = new List<ChatStreamEvent>();
        await foreach (var evt in stream) events.Add(evt);
        return events;
    }

    [Fact]
    public async Task A_thinking_model_on_ollama_is_told_not_to_think_and_how_much_context_to_load()
    {
        var (db, config, streamer) = await Setup(InstanceConfig.Providers.Ollama, "qwen3:4b");
        streamer.EnqueueText("Hi.");

        await CollectAsync(TestChat.Create(db, config, streamer).StreamChatAsync(null, "Hi"));

        var options = Assert.Single(streamer.ReceivedRequests).ModelOptions;
        Assert.False(options.Think);
        Assert.Equal(16_384, options.ContextTokens);
    }

    [Fact]
    public async Task A_model_that_goes_quiet_ends_the_turn_with_an_explanation_instead_of_spinning()
    {
        var (db, config, streamer) = await Setup(InstanceConfig.Providers.Ollama, "qwen2.5:3b-instruct");
        streamer.DelayBeforeFirstChunk = Timeout.InfiniteTimeSpan;
        var time = new FakeTimeProvider();

        var turn = CollectAsync(TestChat.Create(db, config, streamer, time).StreamChatAsync(null, "Hi"));
        while (!turn.IsCompleted)
        {
            time.Advance(TimeSpan.FromSeconds(30));
            await Task.Delay(10);
        }

        var error = Assert.Single(await turn, e => e.Type == "error");
        Assert.Contains("stopped responding", error.Error);
    }

    [Fact]
    public async Task A_small_model_gets_fewer_tool_rounds()
    {
        var (db, config, streamer) = await Setup(InstanceConfig.Providers.Ollama, "qwen2.5:0.5b");
        for (var i = 0; i < 6; i++) streamer.EnqueueToolCall($"t{i}", "get_goals", $$"""{"page":{{i}}}""");

        await CollectAsync(TestChat.Create(db, config, streamer, new FakeTool("get_goals")).StreamChatAsync(null, "Goals?"));

        Assert.Equal(4, streamer.CallCount);
    }

    [Fact]
    public async Task A_long_conversation_is_trimmed_to_the_context_and_the_rest_summarised()
    {
        var (db, config, streamer) = await Setup(InstanceConfig.Providers.Ollama, "qwen2.5:3b-instruct", contextTokens: 4_096);
        var conversation = new Conversation { Title = "Marathon" };
        db.Conversations.Add(conversation);
        var start = DateTime.UtcNow.AddHours(-1);
        for (var i = 0; i < 40; i++)
        {
            db.ChatMessages.Add(new ChatMessage
            {
                Conversation = conversation,
                Role = i % 2 == 0 ? ChatRoles.User : ChatRoles.Assistant,
                Content = $"message {i} " + new string('x', 390),
                CreatedAtUtc = start.AddSeconds(i),
            });
        }
        await db.SaveChangesAsync();
        streamer
            .EnqueueText("The user is training for a marathon in March.")
            .EnqueueText("Keep going!");

        var events = await CollectAsync(TestChat.Create(db, config, streamer).StreamChatAsync(conversation.Id, "Motivate me"));

        Assert.Equal(2, streamer.CallCount);
        var summarising = streamer.ReceivedRequests[0];
        Assert.Empty(summarising.Tools);
        Assert.Contains("message 0 ", summarising.Turns.Single().Content);

        var chat = streamer.ReceivedRequests[1];
        Assert.Contains("The user is training for a marathon in March.", chat.SystemPrompt);
        Assert.True(chat.Turns.Count < 41, $"sent {chat.Turns.Count} turns");
        Assert.Equal("Motivate me", chat.Turns[^1].Content);
        Assert.DoesNotContain(chat.Turns, t => t.Content.StartsWith("message 0 "));

        var stored = await db.Conversations.SingleAsync(c => c.Id == conversation.Id);
        Assert.Equal("The user is training for a marathon in March.", stored.Summary);
        Assert.NotNull(stored.SummarizedThroughMessageId);
        Assert.Contains(events, e => e.Type == "done");
    }

    [Fact]
    public async Task A_failed_summary_does_not_stop_the_reply()
    {
        var (db, config, streamer) = await Setup(InstanceConfig.Providers.Ollama, "qwen2.5:3b-instruct", contextTokens: 4_096);
        var conversation = new Conversation { Title = "Long" };
        db.Conversations.Add(conversation);
        for (var i = 0; i < 40; i++)
        {
            db.ChatMessages.Add(new ChatMessage
            {
                Conversation = conversation,
                Role = i % 2 == 0 ? ChatRoles.User : ChatRoles.Assistant,
                Content = new string('x', 400),
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-60 + i),
            });
        }
        await db.SaveChangesAsync();
        streamer.ThrowOnCall = (1, new AiStreamException("busy"));
        streamer.EnqueueText("unused").EnqueueText("Still here.");

        var events = await CollectAsync(TestChat.Create(db, config, streamer).StreamChatAsync(conversation.Id, "Hi"));

        Assert.Contains(events, e => e.Type == "done");
        Assert.Null((await db.Conversations.SingleAsync(c => c.Id == conversation.Id)).Summary);
    }
}
