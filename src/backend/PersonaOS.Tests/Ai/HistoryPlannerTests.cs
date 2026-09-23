using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Ai.History;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Tests.Ai;

public class HistoryPlannerTests
{
    /// <summary>Messages of a known size: each is 400 characters, about 100 tokens.</summary>
    private static List<ChatMessage> Conversation(int count) => Enumerable.Range(1, count)
        .Select(i => new ChatMessage
        {
            Id = i,
            Role = i % 2 == 1 ? ChatRoles.User : ChatRoles.Assistant,
            Content = new string('x', 399),
        })
        .ToList();

    [Fact]
    public void A_short_conversation_is_sent_whole()
    {
        var plan = HistoryPlanner.Plan(Conversation(6), summarizedThroughId: null, fixedTokens: 2_000, contextTokens: 16_384);

        Assert.Equal(6, plan.Kept.Count);
        Assert.Empty(plan.ToSummarize);
        Assert.False(plan.PromptTooLarge);
    }

    [Fact]
    public void A_long_conversation_keeps_the_newest_messages_that_fit()
    {
        // Room: 8192 - 2048 reply - 3000 fixed = 3144 tokens, about 31 messages; summarising cuts
        // that to 60%, so about 18 stay and the rest are folded into the summary.
        var messages = Conversation(60);

        var plan = HistoryPlanner.Plan(messages, summarizedThroughId: null, fixedTokens: 3_000, contextTokens: 8_192);

        Assert.InRange(plan.Kept.Count, 15, 20);
        Assert.Equal(60, plan.Kept[^1].Id);
        Assert.Equal(messages.Count - plan.Kept.Count, plan.ToSummarize.Count);
        Assert.Equal(plan.Kept[0].Id - 1, plan.ToSummarize[^1].Id);
    }

    [Fact]
    public void Messages_already_in_the_summary_are_not_summarised_again()
    {
        var messages = Conversation(60);
        var first = HistoryPlanner.Plan(messages, null, fixedTokens: 3_000, contextTokens: 8_192);
        var through = first.ToSummarize[^1].Id;

        // Two more messages arrive: the window has room to grow, so nothing new needs summarising.
        messages.AddRange(Conversation(62).Skip(60));
        var next = HistoryPlanner.Plan(messages, through, fixedTokens: 3_000, contextTokens: 8_192);

        Assert.Empty(next.ToSummarize);
        Assert.Equal(62, next.Kept[^1].Id);
    }

    [Fact]
    public void Fallback_replies_are_left_out_they_teach_the_next_turn_nothing()
    {
        var messages = new List<ChatMessage>
        {
            new() { Id = 1, Role = ChatRoles.User, Content = "What are my tasks?" },
            new() { Id = 2, Role = ChatRoles.Assistant, Content = EmptyReplyGuard.NoAnswer },
            new() { Id = 3, Role = ChatRoles.User, Content = "Try again" },
            new() { Id = 4, Role = ChatRoles.Assistant, Content = "You have two." },
        };

        var plan = HistoryPlanner.Plan(messages, null, fixedTokens: 1_000, contextTokens: 16_384);

        Assert.Equal([1L, 3L, 4L], plan.Kept.Select(m => m.Id));
    }

    [Fact]
    public void Says_when_the_prompt_alone_does_not_fit()
    {
        // The trap Ollama sets: a 4,096-token context and a prompt of 8,000 tokens and more.
        var plan = HistoryPlanner.Plan(Conversation(4), null, fixedTokens: 8_000, contextTokens: 4_096);

        Assert.True(plan.PromptTooLarge);
        Assert.Empty(plan.Kept);
    }

    [Fact]
    public void Estimates_about_four_characters_a_token()
    {
        Assert.Equal(0, TokenEstimate.Of(string.Empty));
        Assert.Equal(101, TokenEstimate.Of(new string('x', 400)));
    }
}
