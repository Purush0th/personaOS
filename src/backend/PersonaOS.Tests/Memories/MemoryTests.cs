using System.Text.Json;
using PersonaOS.Application.Ai;
using PersonaOS.Application.Ai.Prompts;
using PersonaOS.Application.Memories;
using PersonaOS.Application.Memories.Tools;
using PersonaOS.Domain.Entities;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Memories;

public class MemoryServiceTests
{
    [Fact]
    public async Task Saves_a_memory_with_its_category_and_conversation()
    {
        var db = TestDbContext.Create();
        var conversation = new Conversation { Title = "Workouts" };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        var saved = await new MemoryService(db).CreateAsync("  Prefers   morning workouts ", "preferences", conversation.Id);

        Assert.Equal("Prefers morning workouts", saved.Content);
        Assert.Equal(MemoryCategories.Preference, saved.Category);
        Assert.Equal(conversation.PublicId, saved.SourceConversationId);
        Assert.Equal("Workouts", saved.SourceConversationTitle);
    }

    [Fact]
    public async Task The_same_thing_said_twice_is_one_memory()
    {
        var db = TestDbContext.Create();
        var service = new MemoryService(db);

        var first = await service.CreateAsync("Lives in Lisbon", null, null);
        var second = await service.CreateAsync("lives in lisbon.", "fact", null);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await service.ListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_memory_needs_text(string content)
    {
        await Assert.ThrowsAsync<MemoryValidationException>(() => new MemoryService(TestDbContext.Create()).CreateAsync(content, null, null));
    }

    [Fact]
    public async Task An_unknown_category_is_refused_with_the_known_ones()
    {
        var ex = await Assert.ThrowsAsync<MemoryValidationException>(
            () => new MemoryService(TestDbContext.Create()).CreateAsync("Likes tea", "hobby", null));

        Assert.Contains("fact, preference, project, decision", ex.Message);
    }

    [Fact]
    public async Task Search_needs_every_word_and_allows_other_endings()
    {
        var db = TestDbContext.Create();
        var service = new MemoryService(db);
        await service.CreateAsync("Prefers morning workouts", "preference", null);
        await service.CreateAsync("Car insurance renews in March", "fact", null);
        await service.CreateAsync("Sold the old car", "fact", null);

        Assert.Equal(["Prefers morning workouts"], (await service.ListAsync("workout")).Select(m => m.Content));
        Assert.Equal(["Car insurance renews in March"], (await service.ListAsync("car insurance")).Select(m => m.Content));
        Assert.Equal(2, (await service.ListAsync(category: "fact")).Count);
    }

    [Fact]
    public async Task Deleting_the_conversation_keeps_the_memory()
    {
        var db = TestDbContext.Create();
        var conversation = new Conversation { Title = "Chat" };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        var service = new MemoryService(db);
        var saved = await service.CreateAsync("Has two kids", null, conversation.Id);

        db.Conversations.Remove(conversation);
        await db.SaveChangesAsync();

        var kept = await service.GetAsync(saved.Id);
        Assert.NotNull(kept);
        Assert.Null(kept.SourceConversationId);
    }
}

public class MemoryRetrievalTests
{
    private static async Task<MemoryService> SeedAsync()
    {
        var service = new MemoryService(TestDbContext.Create());
        await service.CreateAsync("Prefers morning workouts", "preference", null);
        await service.CreateAsync("Wants short answers", "preference", null);
        await service.CreateAsync("Car insurance renews in March", "fact", null);
        await service.CreateAsync("The house move is planned for spring", "project", null);
        return service;
    }

    [Fact]
    public async Task Brings_the_memories_that_share_words_with_the_request_and_the_preferences()
    {
        var service = await SeedAsync();

        var relevant = (await service.RelevantAsync("When does my car insurance renew?", 8)).Select(m => m.Content).ToList();

        Assert.Equal("Car insurance renews in March", relevant[0]);
        Assert.Contains("Wants short answers", relevant);
        Assert.DoesNotContain("The house move is planned for spring", relevant);
    }

    [Fact]
    public async Task Stays_within_the_bound()
    {
        var service = await SeedAsync();

        Assert.Equal(2, (await service.RelevantAsync("car house workouts answers", 2)).Count);
    }

    [Fact]
    public async Task Without_memories_brings_nothing()
    {
        Assert.Empty(await new MemoryService(TestDbContext.Create()).RelevantAsync("anything", 8));
    }
}

public class MemoryToolTests
{
    private static JsonElement Input(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task With_auto_save_on_a_memory_is_saved_straight_away_and_receipted()
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        var streamer = new FakeAiMessageStreamer();
        var chat = new ChatContext();
        var chatService = TestChat.Create(db, config, streamer, chat, new CreateMemoryTool(new MemoryService(db), chat));
        streamer
            .EnqueueToolCall("toolu_1", "create_memory", """{"content":"Prefers morning workouts","category":"preference"}""")
            .EnqueueText("I'll remember that.");

        ChatStreamEvent? done = null;
        await foreach (var evt in chatService.StreamChatAsync(null, "I like working out in the morning")) if (evt.Type == "done") done = evt;

        var memory = Assert.Single(db.Memories);
        Assert.Equal("Prefers morning workouts", memory.Content);
        Assert.NotNull(memory.SourceConversationId);
        var receipt = Assert.Single(done!.Actions!);
        Assert.Equal("Remembered", receipt.Label);
        Assert.Equal("Prefers morning workouts", receipt.Summary);
        Assert.Null(done.Pending);
    }

    [Fact]
    public async Task With_auto_save_off_a_memory_waits_on_a_card()
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        (await config.GetOrCreateAsync()).MemoryAutoSave = false;
        await db.SaveChangesAsync();
        var streamer = new FakeAiMessageStreamer();
        var chat = new ChatContext();
        var chatService = TestChat.Create(db, config, streamer, chat, new CreateMemoryTool(new MemoryService(db), chat));
        streamer
            .EnqueueToolCall("toolu_1", "create_memory", """{"content":"Prefers morning workouts"}""")
            .EnqueueText("Shall I remember that?");

        ChatStreamEvent? done = null;
        await foreach (var evt in chatService.StreamChatAsync(null, "I like working out in the morning")) if (evt.Type == "done") done = evt;

        Assert.Empty(db.Memories);
        var card = Assert.Single(done!.Pending!);
        Assert.Contains("Remember “Prefers morning workouts”", card.Summary);

        await chatService.ConfirmActionAsync(card.Id);
        Assert.NotNull(Assert.Single(db.Memories).SourceConversationId);
    }

    [Fact]
    public void Forgetting_always_waits_on_a_card()
    {
        var tool = new DeleteMemoryTool(new MemoryService(TestDbContext.Create()));

        Assert.True(tool.NeedsConfirmation(new InstanceConfig { MemoryAutoSave = true }));
    }

    [Fact]
    public async Task Search_gives_the_model_ids_to_act_on()
    {
        var db = TestDbContext.Create();
        var service = new MemoryService(db);
        var saved = await service.CreateAsync("Prefers morning workouts", "preference", null);

        var result = await new SearchMemoriesTool(service).ExecuteAsync(Input("""{"query":"workout"}"""));

        Assert.Contains($"\"memoryId\":{saved.Id}", result);
        Assert.Contains("Prefers morning workouts", result);
    }

    [Fact]
    public async Task Updating_a_memory_that_does_not_exist_is_refused_before_any_card()
    {
        var tool = new UpdateMemoryTool(new MemoryService(TestDbContext.Create()));

        var ex = await Assert.ThrowsAsync<MemoryNotFoundException>(() => tool.ValidateAsync(Input("""{"memoryId":"#9","content":"x"}""")));
        Assert.Contains("There is no memory 9", ex.Message);
    }
}

public class MemoryPromptTests
{
    [Fact]
    public async Task The_prompt_carries_the_memories_relevant_to_the_message()
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        await config.GetOrCreateAsync();
        var service = new MemoryService(db);
        await service.CreateAsync("Car insurance renews in March", "fact", null);
        await service.CreateAsync("The house move is planned for spring", "project", null);

        var prompt = await new SystemPromptBuilder(config, db, TestPrompts.Library())
            .BuildAsync(new PromptRequest("When is the car insurance due?"));

        Assert.Contains("- [fact] Car insurance renews in March", prompt);
        Assert.DoesNotContain("house move", prompt);
        Assert.Contains("create_memory", prompt);
        Assert.Contains("saved straight away", prompt);
    }

    [Fact]
    public async Task With_the_module_off_the_prompt_says_nothing_about_memories()
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        var instance = await config.GetOrCreateAsync();
        instance.Features = new Dictionary<string, bool>(instance.Features) { [InstanceConfig.Modules.Memory] = false };
        await db.SaveChangesAsync();
        await new MemoryService(db).CreateAsync("Car insurance renews in March", "fact", null);

        var prompt = await new SystemPromptBuilder(config, db, TestPrompts.Library()).BuildAsync(new PromptRequest("car insurance"));

        Assert.DoesNotContain("Car insurance renews", prompt);
        Assert.DoesNotContain("create_memory", prompt);
    }
}

/// <summary>"I'll remember that." with nothing saved: seen three times running on qwen2.5:3b (2026-09-29).</summary>
public class MemoryClaimTests
{
    [Theory]
    [InlineData("I'll remember that.", true)]
    [InlineData("Got it, I've noted that you prefer mornings.", true)]
    [InlineData("I will keep that in mind.", true)]
    [InlineData("Noted!", true)]
    [InlineData("I remember you said you prefer mornings.", false)]
    [InlineData("Would you like me to remember that?", false)]
    [InlineData("I can remember that if you like.", false)]
    public void Finds_a_promise_to_remember(string reply, bool claim)
    {
        Assert.Equal(claim, ActionClaimDetector.FindMemoryClaim(reply) is not null);
    }

    [Fact]
    public async Task A_promise_to_remember_with_nothing_saved_gets_the_call_it_promised()
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        var streamer = new FakeAiMessageStreamer();
        var chat = new ChatContext();
        var chatService = TestChat.Create(db, config, streamer, chat, new CreateMemoryTool(new MemoryService(db), chat));
        streamer
            .EnqueueText("I'll remember that.")
            .EnqueueToolCall("toolu_1", "create_memory", """{"content":"Prefers morning workouts","category":"preference"}""")
            .EnqueueText("Saved: you prefer morning workouts.");

        ChatStreamEvent? done = null;
        await foreach (var evt in chatService.StreamChatAsync(null, "I prefer working out in the mornings")) if (evt.Type == "done") done = evt;

        Assert.Equal("Prefers morning workouts", Assert.Single(db.Memories).Content);
        Assert.Equal("Saved: you prefer morning workouts.", done!.Text);
        Assert.Null(done.UnverifiedClaim);
    }

    [Fact]
    public async Task With_the_module_off_remember_about_user_is_offered_instead()
    {
        var db = TestDbContext.Create();
        var config = new FakeInstanceConfigService(db);
        var instance = await config.GetOrCreateAsync();
        var tool = new PersonaOS.Application.Profile.RememberAboutUserTool(new PersonaOS.Application.Profile.UserProfileService(db));
        PersonaOS.Application.Ai.Tools.IPersonaTool asTool = tool;

        Assert.False(asTool.IsOffered(instance));
        instance.Features = new Dictionary<string, bool>(instance.Features) { [InstanceConfig.Modules.Memory] = false };
        Assert.True(asTool.IsOffered(instance));
    }
}
