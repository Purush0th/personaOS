using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;
using PersonaOS.Infrastructure.Ai;

namespace PersonaOS.Tests.Ai;

/// <summary>
/// The OpenAI-compatible adapter against a stub server: the request it sends, and how it reads a
/// streamed reply.
/// </summary>
public class OpenAiCompatibleMessageStreamerTests
{
    private sealed class StubServer(params string[] events) : HttpMessageHandler
    {
        public Uri? Url { get; private set; }
        public JsonObject? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri;
            Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            var sse = string.Concat(events.Select(e => $"data: {e}\n\n")) + "data: [DONE]\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
            };
        }
    }

    private static AiRequest Request(AiModelOptions? options = null) => new(
        ApiKey: string.Empty,
        Model: "qwen2.5:3b-instruct",
        BaseUrl: "http://gpu-box:11434/v1/",
        SystemPrompt: "You are Juno.",
        Turns: [new AiChatTurn(ChatRoles.User, "What are my goals?")],
        Tools: [],
        Options: options);

    private static async Task<string> Text(StubServer server, AiRequest request)
    {
        var text = new StringBuilder();
        await foreach (var chunk in new OpenAiCompatibleMessageStreamer(new HttpClient(server)).StreamAsync(request))
            text.Append(chunk.TextDelta);
        return text.ToString();
    }

    [Fact]
    public async Task Sends_the_temperature_and_reads_the_streamed_text()
    {
        var server = new StubServer("""{"choices":[{"delta":{"content":"Two "}}]}""", """{"choices":[{"delta":{"content":"goals."}}]}""");

        var text = await Text(server, Request(new AiModelOptions(ContextTokens: 16384, Temperature: 0.2)));

        Assert.Equal("Two goals.", text);
        Assert.Equal("http://gpu-box:11434/v1/chat/completions", server.Url!.ToString());
        Assert.Equal(0.2, server.Body!["temperature"]!.GetValue<double>());
        // Chat Completions has no field for a context size; it is never sent.
        Assert.False(server.Body.ContainsKey("num_ctx"));
        Assert.False(server.Body.ContainsKey("options"));
    }

    [Fact]
    public async Task Leaves_the_temperature_to_the_server_when_none_is_set()
    {
        var server = new StubServer("""{"choices":[{"delta":{"content":"Hi"}}]}""");

        await Text(server, Request());

        Assert.False(server.Body!.ContainsKey("temperature"));
    }
}
