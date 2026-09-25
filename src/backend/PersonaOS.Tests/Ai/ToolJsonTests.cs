using PersonaOS.Application.Ai.Tools;

namespace PersonaOS.Tests.Ai;

public class ToolJsonTests
{
    private sealed record Goal(
        string Key, string? Description, int Progress, IReadOnlyList<string> Tasks,
        int CommentCount, int AttachmentCount, int SortOrder);

    [Fact]
    public void Leaves_out_what_a_small_model_would_invent_from_or_read_out()
    {
        var json = ToolJson.Serialize(new { goals = new[] { new Goal("GOAL-2", null, 0, [], 0, 0, 3) } });

        // A progress of zero is a fact; no description, no tasks and no comments are not worth a word.
        Assert.Equal("""{"goals":[{"key":"GOAL-2","progress":0}]}""", json);
    }

    [Fact]
    public void Keeps_what_has_a_value()
    {
        var json = ToolJson.Serialize(new { goal = new Goal("GOAL-2", "Eat better", 40, ["TASK-7"], 2, 1, 3) });

        Assert.Equal(
            """{"goal":{"key":"GOAL-2","description":"Eat better","progress":40,"tasks":["TASK-7"],"commentCount":2,"attachmentCount":1}}""",
            json);
    }

    [Fact]
    public void Keeps_a_tools_own_empty_list_so_no_goals_is_still_an_answer()
    {
        Assert.Equal("""{"goals":[]}""", ToolJson.Serialize(new { goals = Array.Empty<Goal>() }));
    }
}
