using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;
using PersonaOS.Infrastructure.Ai;

namespace PersonaOS.Tests.Ai;

/// <summary>
/// The native Ollama adapter against a stub server: what it sends (including the options the
/// OpenAI-compatible endpoint cannot carry) and how it reads the newline-delimited reply.
/// </summary>
public class OllamaMessageStreamerTests
{
    private sealed class StubServer(HttpStatusCode status, params string[] lines) : HttpMessageHandler
    {
        public Uri? Url { get; private set; }
        public JsonObject? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri;
            Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(string.Join("\n", lines), Encoding.UTF8, "application/x-ndjson"),
            };
        }
    }

    private static AiRequest Request(AiModelOptions? options = null, IReadOnlyList<AiChatTurn>? turns = null) => new(
        ApiKey: string.Empty,
        Model: "qwen3:4b",
        BaseUrl: "http://gpu-box:11434/v1/",
        SystemPrompt: "You are Juno.",
        Turns: turns ?? [new AiChatTurn(ChatRoles.User, "What are my goals?")],
        Tools: [new AiToolDefinition("get_goals", "Lists goals.", """{"type":"object","properties":{}}""")],
        Options: options);

    private static async Task<List<AiStreamChunk>> Collect(StubServer server, AiRequest request)
    {
        var chunks = new List<AiStreamChunk>();
        await foreach (var chunk in new OllamaMessageStreamer(new HttpClient(server)).StreamAsync(request)) chunks.Add(chunk);
        return chunks;
    }

    [Fact]
    public async Task Sends_the_options_the_compatible_endpoint_cannot_carry()
    {
        var server = new StubServer(HttpStatusCode.OK, """{"message":{"content":"Hi"},"done":true}""");

        await Collect(server, Request(new AiModelOptions(ContextTokens: 16384, Think: false, KeepAlive: "30m")));

        Assert.Equal("http://gpu-box:11434/api/chat", server.Url!.ToString());
        Assert.Equal(16384, server.Body!["options"]!["num_ctx"]!.GetValue<int>());
        Assert.False(server.Body["think"]!.GetValue<bool>());
        Assert.Equal("30m", server.Body["keep_alive"]!.GetValue<string>());
        Assert.True(server.Body["stream"]!.GetValue<bool>());
        Assert.Equal("get_goals", server.Body["tools"]![0]!["function"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Leaves_out_options_that_were_not_asked_for()
    {
        var server = new StubServer(HttpStatusCode.OK, """{"message":{"content":"Hi"},"done":true}""");

        await Collect(server, Request());

        Assert.False(server.Body!.ContainsKey("think"));
        Assert.False(server.Body.ContainsKey("options"));
        Assert.False(server.Body.ContainsKey("keep_alive"));
    }

    [Fact]
    public async Task Streams_text_and_reports_usage_at_the_end()
    {
        var server = new StubServer(HttpStatusCode.OK,
            """{"message":{"role":"assistant","content":"You have "},"done":false}""",
            """{"message":{"role":"assistant","content":"two goals."},"done":false}""",
            """{"message":{"role":"assistant","content":""},"done":true,"prompt_eval_count":812,"eval_count":9}""");

        var chunks = await Collect(server, Request());

        Assert.Equal("You have two goals.", string.Concat(chunks.Select(c => c.TextDelta)));
        var last = chunks[^1];
        Assert.Equal(812, last.InputTokens);
        Assert.Equal(9, last.OutputTokens);
        Assert.Null(last.StopReason);
    }

    [Fact]
    public async Task Hands_back_tool_calls_with_their_arguments_as_json()
    {
        var server = new StubServer(HttpStatusCode.OK,
            """{"message":{"content":"","tool_calls":[{"function":{"name":"get_goals","arguments":{"status":"active"}}}]},"done":false}""",
            """{"message":{"content":""},"done":true,"done_reason":"stop"}""");

        var chunks = await Collect(server, Request());

        var call = Assert.Single(chunks, c => c.ToolCall is not null).ToolCall!;
        Assert.Equal("get_goals", call.Name);
        Assert.Equal("""{"status":"active"}""", call.InputJson);
        Assert.False(string.IsNullOrEmpty(call.Id));
        Assert.Equal(AiStopReasons.ToolUse, chunks[^1].StopReason);
    }

    [Fact]
    public async Task Pairs_a_tool_result_with_the_tool_it_answers_by_name()
    {
        var turns = new List<AiChatTurn>
        {
            new(ChatRoles.User, "Goals?"),
            new(ChatRoles.Assistant, string.Empty, ToolCalls: [new AiToolCall("call_1", "get_goals", """{"status":"active"}""")]),
            new(ChatRoles.User, string.Empty, ToolResults: [new AiToolResult("call_1", "[]")]),
        };
        var server = new StubServer(HttpStatusCode.OK, """{"message":{"content":"None."},"done":true}""");

        await Collect(server, Request(turns: turns));

        var messages = server.Body!["messages"]!.AsArray();
        Assert.Equal("active", messages[2]!["tool_calls"]![0]!["function"]!["arguments"]!["status"]!.GetValue<string>());
        Assert.Equal("tool", messages[3]!["role"]!.GetValue<string>());
        Assert.Equal("get_goals", messages[3]!["tool_name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Explains_a_model_without_tool_support()
    {
        var server = new StubServer(HttpStatusCode.BadRequest,
            """{"error":"registry.ollama.ai/library/qwen2:1.5b does not support tools"}""");

        var error = await Assert.ThrowsAsync<AiStreamException>(() => Collect(server, Request()));

        Assert.Contains("cannot use tools", error.Message);
    }

    [Fact]
    public async Task Says_how_to_get_a_model_the_server_does_not_have()
    {
        var server = new StubServer(HttpStatusCode.NotFound, """{"error":"model \"qwen3:4b\" not found, try pulling it first"}""");

        var error = await Assert.ThrowsAsync<AiStreamException>(() => Collect(server, Request()));

        Assert.Contains("ollama pull qwen3:4b", error.Message);
    }

    [Fact]
    public async Task Surfaces_an_error_the_server_reports_mid_stream()
    {
        var server = new StubServer(HttpStatusCode.OK,
            """{"message":{"content":"Par"},"done":false}""",
            """{"error":"model runner has unexpectedly stopped"}""");

        var error = await Assert.ThrowsAsync<AiStreamException>(() => Collect(server, Request()));

        Assert.Contains("unexpectedly stopped", error.Message);
    }

    [Fact]
    public async Task Refuses_to_run_without_an_address()
    {
        var request = Request() with { BaseUrl = " " };

        await Assert.ThrowsAsync<AiStreamException>(() => Collect(new StubServer(HttpStatusCode.OK), request));
    }
}
