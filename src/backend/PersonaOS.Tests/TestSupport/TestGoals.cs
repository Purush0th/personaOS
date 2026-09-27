using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Board;
using PersonaOS.Application.Goals;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Tests.TestSupport;

/// <summary>
/// A <see cref="GoalService"/> on a fixed clock, and goal requests by calendar slot. Goals never
/// start in the past, so every goal test pins "today"; the default is 1 September 2026 (UTC,
/// which is the fake config's zone).
/// </summary>
public static class TestGoals
{
    public static readonly DateTimeOffset Today = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    public static GoalService Service(TestDbContext db, TimeProvider? clock = null)
    {
        clock ??= new FixedTimeProvider(Today);
        var config = new FakeInstanceConfigService(db);
        var board = new BoardService(db, config, new FakePushSender(), clock, NullLogger<BoardService>.Instance);
        return new GoalService(db, config, board, clock);
    }

    public static CreateGoalRequest Year(string title, int year = 2026) =>
        new(title, null, GoalPeriods.Year, year);

    public static CreateGoalRequest Quarter(string title, int quarter, int year = 2026, int? parentId = null) =>
        new(title, null, GoalPeriods.Quarter, year, Quarter: quarter, ParentId: parentId);

    public static CreateGoalRequest Month(string title, int month = 9, int year = 2026, int? parentId = null) =>
        new(title, null, GoalPeriods.Month, year, Month: month, ParentId: parentId);
}
