using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using PersonaOS.Application.Ai.Guards;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Ai.Guards;

public class ToolCallGuardTests
{
    private static ChatTurnState Turn(params string[] mutating) =>
        new("test-model", mutating.ToHashSet(), ["get_goals", "create_goal"]);

    private static PersonaToolRegistry Registry(params IPersonaTool[] tools)
    {
        var db = TestDbContext.Create();
        return new PersonaToolRegistry(tools, new FakeInstanceConfigService(db), NullLogger<PersonaToolRegistry>.Instance);
    }

    private static ToolCallPipeline Pipeline(params IPersonaTool[] tools) =>
        new(Registry(tools), NullLogger<ToolCallPipeline>.Instance);

    [Fact]
    public async Task Envelope_guard_unwraps_nested_arguments_and_leaves_bare_ones_alone()
    {
        var guard = new ArgumentEnvelopeGuard();
        var wrapped = new AiToolCall("1", "create_goal", """{"function":"create_goal","arguments":{"title":"Run"}}""");
        var bare = new AiToolCall("2", "create_goal", """{"title":"Run"}""");

        var verdict = Assert.IsType<ToolCallVerdict.Rewrite>(await guard.CheckAsync(wrapped, Turn(), default));
        Assert.Equal("""{"title":"Run"}""", verdict.Call.InputJson);
        Assert.Null(await guard.CheckAsync(bare, Turn(), default));
    }

    [Fact]
    public async Task Repeat_guard_answers_a_call_already_made_and_counts_it()
    {
        var guard = new RepeatedCallGuard();
        var turn = Turn();
        var call = new AiToolCall("1", "get_goals", "{}");

        Assert.Null(await guard.CheckAsync(call, turn, default));
        turn.RecordExecution(call, new AiToolResult("1", "[goals]"));

        var verdict = Assert.IsType<ToolCallVerdict.Answer>(await guard.CheckAsync(call with { Id = "2" }, turn, default));
        Assert.StartsWith("[goals]", verdict.Content);
        Assert.Contains(RepeatedCallGuard.Note, verdict.Content);
        Assert.Equal(1, turn.Repeats);
        Assert.False(turn.IsStuck);
    }

    [Fact]
    public async Task Repeat_guard_matches_on_arguments_not_just_the_tool()
    {
        var turn = Turn();
        turn.RecordExecution(new AiToolCall("1", "get_goals", """{"status":"active"}"""), new AiToolResult("1", "a"));

        Assert.Null(await new RepeatedCallGuard().CheckAsync(new AiToolCall("2", "get_goals", """{"status":"done"}"""), turn, default));
    }

    [Fact]
    public async Task Gate_lets_a_read_through()
    {
        var gate = new ConfirmationGate(Registry(new FakeTool("get_goals")));

        Assert.Null(await gate.CheckAsync(new AiToolCall("1", "get_goals", "{}"), Turn("create_goal"), default));
    }

    [Fact]
    public async Task Gate_turns_a_write_into_a_proposal_and_tells_the_model_it_did_not_run()
    {
        var create = new FakeTool("create_goal", mutates: true);
        var turn = Turn("create_goal");
        var call = new AiToolCall("1", "create_goal", """{"title":"Run"}""");

        var verdict = Assert.IsType<ToolCallVerdict.Answer>(await new ConfirmationGate(Registry(create)).CheckAsync(call, turn, default));

        Assert.False(verdict.IsError);
        Assert.Contains("not_executed", verdict.Content);
        var proposal = Assert.Single(turn.Proposals);
        Assert.Equal("create_goal", proposal.ToolName);
        Assert.Empty(create.Invocations);
        // Remembered, so the same proposal again is a repeat, not a second card.
        Assert.True(turn.TryRecall(call, out _));
    }

    [Fact]
    public async Task Gate_refuses_a_write_the_tool_says_is_invalid_and_proposes_nothing()
    {
        var create = new FakeTool("create_goal", mutates: true) { ValidationError = "GOAL-99 does not exist." };
        var turn = Turn("create_goal");

        var verdict = Assert.IsType<ToolCallVerdict.Answer>(
            await new ConfirmationGate(Registry(create)).CheckAsync(new AiToolCall("1", "create_goal", "{}"), turn, default));

        Assert.True(verdict.IsError);
        Assert.Contains("GOAL-99 does not exist.", verdict.Content);
        Assert.Empty(turn.Proposals);
    }

    [Fact]
    public async Task Pipeline_unwraps_before_checking_for_a_repeat()
    {
        // A repeat is only recognisable once both calls are unwrapped the same way.
        var turn = Turn();
        var pipeline = Pipeline(new FakeTool("get_goals"));
        turn.RecordExecution(new AiToolCall("1", "get_goals", """{"status":"active"}"""), new AiToolResult("1", "a"));

        var decision = await pipeline.DecideAsync(
            new AiToolCall("2", "get_goals", """{"function":"get_goals","arguments":{"status":"active"}}"""), turn, default);

        Assert.IsType<ToolCallDecision.Answered>(decision);
        Assert.Equal("""{"status":"active"}""", decision.Call.InputJson);
    }

    [Fact]
    public async Task Pipeline_runs_a_call_no_guard_objects_to()
    {
        var decision = await Pipeline(new FakeTool("get_goals")).DecideAsync(new AiToolCall("1", "get_goals", "{}"), Turn(), default);

        Assert.IsType<ToolCallDecision.Run>(decision);
    }

    [Fact]
    public async Task Every_firing_is_counted_by_guard_and_model()
    {
        // The meter is static, so tests running in parallel report to this listener too, from
        // other threads: collect thread-safely and look only for this test's own model name.
        const string model = "metric-test-model";
        var fired = new System.Collections.Concurrent.ConcurrentBag<(long Value, string? Guard, string? Model)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == GuardTelemetry.MeterName) l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var copied = tags.ToArray();
            fired.Add((value,
                copied.FirstOrDefault(t => t.Key == "guard").Value as string,
                copied.FirstOrDefault(t => t.Key == "model").Value as string));
        });
        listener.Start();

        await Pipeline(new FakeTool("create_goal", mutates: true))
            .DecideAsync(new AiToolCall("1", "create_goal", "{}"), new ChatTurnState(model, new HashSet<string> { "create_goal" }, []), default);

        Assert.Contains((1L, "confirmation-gate", model), fired);
    }
}
