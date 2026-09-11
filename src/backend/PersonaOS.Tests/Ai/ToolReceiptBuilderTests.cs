using PersonaOS.Application.Ai;

namespace PersonaOS.Tests.Ai;

/// <summary>
/// Receipts summarise what a tool actually stored. They exist because qwen2.5 created a
/// 6pm local reminder correctly and then told the user it was 6pm UTC — the write was
/// right and the sentence was wrong, which only the stored values can expose.
/// </summary>
public class ToolReceiptBuilderTests
{
    [Fact]
    public void Shows_the_stored_local_time_for_a_created_reminder()
    {
        // Exactly the case that was misdescribed: stored 18:00 local / 12:30 UTC.
        const string result = """
            {"id":1,"message":"Travel to home","dueAtUtc":"2026-09-06T12:30:00",
             "dueAtLocal":"2026-09-06T18:00:00","status":"pending"}
            """;

        var receipt = ToolReceiptBuilder.Build("create_reminder", result, isError: false);

        Assert.Equal("create_reminder", receipt.Tool);
        Assert.True(receipt.Ok);
        Assert.Contains("Travel to home", receipt.Summary);
        Assert.Contains("2026-09-06 18:00", receipt.Summary);
        // The receipt must not quietly show UTC — that is the confusion it exists to prevent.
        Assert.DoesNotContain("12:30", receipt.Summary);
    }

    [Fact]
    public void Shows_title_and_date_for_a_created_planner_item()
    {
        const string result = """
            {"id":2,"title":"Buy milk","date":"2026-09-06","status":"planned","scheduledTime":null}
            """;

        var receipt = ToolReceiptBuilder.Build("add_planner_item", result, isError: false);

        Assert.Contains("Buy milk", receipt.Summary);
        Assert.Contains("2026-09-06", receipt.Summary);
        Assert.Contains("planned", receipt.Summary);
    }

    [Fact]
    public void Sees_through_the_wrapper_create_goal_returns()
    {
        // Observed 2026-09-11: create_goal returns {"created": {…}}, so the receipt came back
        // with a null summary — and the user had nothing to check the reply against at the exact
        // moment the model got the period wrong and skipped the parent link.
        const string result = """
            {"created":{"id":2,"title":"Learn C# for AI applications","description":null,
             "parentGoalId":null,"periodType":"month","periodStart":"2026-10-01",
             "status":"active","progress":0}}
            """;

        var receipt = ToolReceiptBuilder.Build("create_goal", result, isError: false);

        Assert.Contains("Learn C# for AI applications", receipt.Summary);
        // The period must be visible: seeing "2026-10-01" is what reveals a goal filed
        // under the wrong month.
        Assert.Contains("2026-10-01", receipt.Summary);
        Assert.Contains("month", receipt.Summary);
    }

    [Fact]
    public void Sees_through_a_wrapper_around_a_collection()
    {
        // get_goals returns {"goals": [...]}.
        var receipt = ToolReceiptBuilder.Build(
            "get_goals", """{"goals":[{"id":1},{"id":2},{"id":3}]}""", isError: false);

        Assert.Equal("3 items", receipt.Summary);
    }

    [Fact]
    public void Does_not_unwrap_a_single_scalar_property()
    {
        var receipt = ToolReceiptBuilder.Build("some_tool", """{"ok":true}""", isError: false);

        Assert.Null(receipt.Summary);
    }

    [Fact]
    public void Reports_how_many_rows_a_read_returned()
    {
        var receipt = ToolReceiptBuilder.Build("get_goals", """[{"id":1},{"id":2}]""", isError: false);

        Assert.Equal("2 items", receipt.Summary);
    }

    [Fact]
    public void Uses_the_singular_for_one_row()
    {
        var receipt = ToolReceiptBuilder.Build("get_goals", """[{"id":1}]""", isError: false);

        Assert.Equal("1 item", receipt.Summary);
    }

    [Fact]
    public void Marks_a_failed_tool_and_keeps_its_message()
    {
        var receipt = ToolReceiptBuilder.Build("cancel_reminder", "Reminder 99 does not exist.", isError: true);

        Assert.False(receipt.Ok);
        Assert.Equal("Reminder 99 does not exist.", receipt.Summary);
    }

    [Fact]
    public void Falls_back_to_no_summary_when_the_result_has_nothing_to_show()
    {
        var receipt = ToolReceiptBuilder.Build("some_tool", """{"ok":true}""", isError: false);

        Assert.True(receipt.Ok);
        Assert.Null(receipt.Summary);
    }

    [Fact]
    public void Survives_a_result_that_is_not_json()
    {
        var receipt = ToolReceiptBuilder.Build("some_tool", "done", isError: false);

        Assert.True(receipt.Ok);
        Assert.Null(receipt.Summary);
    }

    [Fact]
    public void Renders_a_date_only_value_without_a_midnight_time()
    {
        const string result = """{"title":"Ship v1","date":"2026-09-06T00:00:00"}""";

        var receipt = ToolReceiptBuilder.Build("add_planner_item", result, isError: false);

        Assert.Contains("2026-09-06", receipt.Summary);
        Assert.DoesNotContain("00:00", receipt.Summary);
    }

    [Fact]
    public void Truncates_an_overlong_summary()
    {
        var longTitle = new string('x', 400);
        var receipt = ToolReceiptBuilder.Build(
            "add_planner_item", $$"""{"title":"{{longTitle}}"}""", isError: false);

        Assert.True(receipt.Summary!.Length <= 160);
        Assert.EndsWith("…", receipt.Summary);
    }
}
