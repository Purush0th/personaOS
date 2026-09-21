using System.Text.Json;
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
