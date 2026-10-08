using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Planner;
using PersonaOS.Application.Planner.Tools;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Planner;

/// <summary>
/// The planner tools are what the assistant answers "what am I doing today?" with, so the
/// common call — no arguments at all — has to work rather than send the model guessing.
/// </summary>
public class PlannerToolsTests
{
    private static (TestDbContext Db, GetPlannerTool Tool) Setup()
    {
        var db = TestDbContext.Create();
        return (db, new GetPlannerTool(new PlannerService(db), new FakeInstanceConfigService(db)));
    }

    private static JsonElement Input(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task No_arguments_reads_today()
    {
        // Reported live: five "Provide 'date'…" failures in a row, then a date from 2023.
        var (db, tool) = Setup();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await new PlannerService(db).CreateAsync(new CreatePlannerItemRequest("Write the letter", today));

        var result = await tool.ExecuteAsync(Input("{}"));

        Assert.Contains(today.ToString("yyyy-MM-dd"), result);
        Assert.Contains("Write the letter", result);
    }

    [Fact]
    public async Task A_date_still_wins_over_today()
    {
        var (db, tool) = Setup();
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        await new PlannerService(db).CreateAsync(new CreatePlannerItemRequest("Post the parcel", tomorrow));

        var result = await tool.ExecuteAsync(Input($$"""{"date":"{{tomorrow:yyyy-MM-dd}}"}"""));

        Assert.Contains("Post the parcel", result);
    }

    private static AddPlannerItemTool AddTool(TestDbContext db)
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero));
        var config = new FakeInstanceConfigService(db);
        var board = new PersonaOS.Application.Board.BoardService(
            db, config, new FakePushSender(), clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PersonaOS.Application.Board.BoardService>.Instance);
        return new AddPlannerItemTool(new PlannerService(db), TestGoals.Service(db, clock), board, config);
    }

    [Fact]
    public async Task A_timed_event_gets_the_usual_heads_up_and_the_card_says_so()
    {
        var tool = AddTool(TestDbContext.Create());

        var timed = await tool.CompleteInputAsync(Input("""{"title":"Customer calls","date":"2026-10-03","scheduledTime":"16:00","endTime":"17:00"}"""));
        var allDay = await tool.CompleteInputAsync(Input("""{"title":"Durga Puja","date":"2026-10-17"}"""));

        Assert.Contains("\"notifyBeforeMinutes\":30", timed);
        Assert.DoesNotContain("notifyBeforeMinutes", allDay);
        Assert.Equal(
            "Add planner item “Customer calls” — 2026-10-03 · 16:00 · until 17:00 · notify 30 minutes before",
            PersonaOS.Application.Ai.ProposedActionSummary.Describe("add_planner_item", timed));
        Assert.EndsWith("2026-10-17 · all day", PersonaOS.Application.Ai.ProposedActionSummary.Describe("add_planner_item", allDay));
    }

    [Fact]
    public async Task A_repeating_event_is_saved_with_its_rule_and_the_card_shows_it()
    {
        var db = TestDbContext.Create();
        var tool = AddTool(db);
        var input = Input("""{"title":"Orders collection","date":"2026-10-03","scheduledTime":"08:30","endTime":"09:30","repeat":"weekly","repeatUntil":"2026-12-26","notifyBeforeMinutes":-1}""");

        await tool.ValidateAsync(input);
        await tool.ExecuteAsync(input);

        var item = await db.PlannerItems.SingleAsync();
        Assert.Equal("FREQ=WEEKLY;UNTIL=20261226", item.Recurrence);
        Assert.Equal(new TimeOnly(9, 30), item.EndTime);
        Assert.Null(item.NotifyBeforeMinutes);
        Assert.Contains(
            "repeats weekly until 2026-12-26 · no notification",
            PersonaOS.Application.Ai.ProposedActionSummary.Describe("add_planner_item", input.GetRawText()));
    }

    [Theory]
    [InlineData("""{"title":"Buy milk","endTime":"10:00"}""")]
    [InlineData("""{"title":"Buy milk","repeat":"none"}""")]
    [InlineData("""{"title":"Buy milk","repeat":"never","repeatUntil":"2026-12-01"}""")]
    [InlineData("""{"title":"Buy milk","scheduledTime":"","endTime":"","notes":""}""")]
    public async Task Optional_fields_a_small_model_fills_in_wrongly_are_dropped_not_refused(string json)
    {
        // Accuracy suite, 2026-10-07: refusing these made qwen2.5:3b ask for a start time or a
        // period instead of proposing "Buy milk" (8 of 10).
        var db = TestDbContext.Create();
        var tool = AddTool(db);

        var completed = Input(await tool.CompleteInputAsync(Input(json)));
        await tool.ValidateAsync(completed);
        await tool.ExecuteAsync(completed);

        var item = await db.PlannerItems.SingleAsync();
        Assert.Null(item.ScheduledTime);
        Assert.Null(item.EndTime);
        Assert.Null(item.Recurrence);
        Assert.Equal("all day", PersonaOS.Application.Ai.ProposedActionSummary.Describe("add_planner_item", completed.GetRawText()).Split(" · ")[^1]);
    }

    [Fact]
    public async Task An_event_over_several_days_is_saved_with_its_last_day_and_the_card_shows_it()
    {
        var db = TestDbContext.Create();
        var tool = AddTool(db);

        var allDay = Input(await tool.CompleteInputAsync(Input("""{"title":"Goa trip","date":"2026-10-09","endDate":"2026-10-11"}""")));
        var timed = await tool.CompleteInputAsync(Input("""{"title":"Conference","date":"2026-10-09","scheduledTime":"18:00","endTime":"09:00","endDate":"2026-10-10"}"""));
        await tool.ValidateAsync(allDay);
        await tool.ValidateAsync(Input(timed));
        await tool.ExecuteAsync(allDay);

        Assert.Equal(new DateOnly(2026, 10, 11), (await db.PlannerItems.SingleAsync()).EndDate);
        Assert.Equal(
            "Add planner item “Goa trip” — 2026-10-09 · until 2026-10-11 · all day",
            PersonaOS.Application.Ai.ProposedActionSummary.Describe("add_planner_item", allDay.GetRawText()));
        Assert.Contains("until 2026-10-10 09:00", PersonaOS.Application.Ai.ProposedActionSummary.Describe("add_planner_item", timed));
    }

    [Theory]
    [InlineData("""{"title":"Buy milk","date":"2026-10-09","endDate":"2026-10-09"}""")]
    [InlineData("""{"title":"Buy milk","date":"2026-10-09","endDate":"2026-10-01"}""")]
    [InlineData("""{"title":"Buy milk","date":"2026-10-09","endDate":"tomorrow"}""")]
    [InlineData("""{"title":"Buy milk","date":"2026-10-09","endDate":""}""")]
    public async Task A_last_day_that_is_not_after_the_first_is_dropped(string json)
    {
        var tool = AddTool(TestDbContext.Create());

        var completed = await tool.CompleteInputAsync(Input(json));

        Assert.DoesNotContain("endDate", completed);
    }

    [Theory]
    [InlineData("""{"title":"x","scheduledTime":"10:00","endTime":"09:00"}""", "later than")]
    [InlineData("""{"title":"x","date":"2026-10-09","scheduledTime":"18:00","endDate":"2026-10-10"}""", "needs an 'endTime'")]
    [InlineData("""{"title":"x","repeat":"hourly"}""", "daily, weekly")]
    [InlineData("""{"title":"x","repeatUntil":"2026-12-01"}""", "only goes with")]
    [InlineData("""{"title":"x","date":"2026-10-10","repeat":"daily","repeatUntil":"2026-10-01"}""", "before the first day")]
    [InlineData("""{"title":"x","scheduledTime":"10:00","notifyBeforeMinutes":50000}""", "at most 4 weeks")]
    public async Task An_event_that_could_not_be_saved_is_refused_before_the_card(string json, string reason)
    {
        var tool = AddTool(TestDbContext.Create());

        var error = await Assert.ThrowsAsync<PlannerValidationException>(() => tool.ValidateAsync(Input(json)));

        Assert.Contains(reason, error.Message);
    }

    [Fact]
    public async Task Marking_a_repeating_event_done_needs_the_day_and_marks_only_that_one()
    {
        var db = TestDbContext.Create();
        var planner = new PlannerService(db);
        var series = await planner.CreateAsync(new CreatePlannerItemRequest(
            "Orders collection", new DateOnly(2026, 10, 3), Recurrence: "FREQ=WEEKLY"));
        var tool = new UpdatePlannerItemStatusTool(planner);

        var error = await Assert.ThrowsAsync<PlannerValidationException>(
            () => tool.ValidateAsync(Input($$"""{"itemId":{{series.Id}},"status":"done"}""")));
        Assert.Contains("pass occurrenceDate", error.Message);
        Assert.Contains("Weekly on Saturday", error.Message);

        var input = Input($$"""{"itemId":{{series.Id}},"occurrenceDate":"2026-10-10","status":"done"}""");
        await tool.ValidateAsync(input);
        await tool.ExecuteAsync(input);

        var october = (await planner.GetRangeAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 17))).SelectMany(d => d.Items);
        Assert.Equal(["planned", "done", "planned"], october.Select(i => i.Status));
    }

    [Fact]
    public async Task Today_lists_a_repeating_event_with_its_repeat_in_words()
    {
        var (db, tool) = Setup();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await new PlannerService(db).CreateAsync(new CreatePlannerItemRequest("Stand-up", today.AddDays(-7), Recurrence: "FREQ=DAILY"));

        var result = await tool.ExecuteAsync(Input("{}"));

        Assert.Contains("Stand-up", result);
        Assert.Contains("Daily", result);
        Assert.Contains($"\"occurrenceDate\":\"{today:yyyy-MM-dd}\"", result);
    }

    [Fact]
    public async Task Half_a_range_is_still_refused()
    {
        // "from" without "to" has no sensible reading, unlike an empty call.
        var (_, tool) = Setup();

        var error = await Assert.ThrowsAsync<PlannerValidationException>(
            () => tool.ExecuteAsync(Input("""{"from":"2026-09-21"}""")));

        Assert.Contains("both 'from' and 'to'", error.Message);
    }
}
