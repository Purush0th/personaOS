using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PersonaOS.Application.Common.Interfaces;

namespace PersonaOS.Infrastructure.Speech;

/// <summary>
/// Both speech ports over the OpenAI-style audio API: <c>POST {base}/audio/transcriptions</c>
/// (multipart: file, model) and <c>POST {base}/audio/speech</c> (JSON: model, input, voice). The
/// same API is offered by OpenAI, faster-whisper servers (Speaches) and Kokoro-FastAPI, so one
/// adapter covers a cloud service and a self-hosted one alike. The key, when there is one, goes
/// only in the Authorization header and never into a message.
/// </summary>
public sealed class OpenAiSpeechClient(HttpClient http) : ISpeechToText, ITextToSpeech
{
    public async Task<string> TranscribeAsync(
        SpeechEndpoint endpoint, string model, Stream audio, string fileName, string? language, CancellationToken ct = default)
    {
        // Buffered, so the request carries a Content-Length: a chunked upload is refused or read
        // as empty by simpler servers. A recording is small (the API caps it at 25 MB).
        using var buffered = new MemoryStream();
        await audio.CopyToAsync(buffered, ct);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(buffered.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue(ContentTypeOf(fileName));
        form.Add(file, "file", fileName);
        form.Add(new StringContent(model), "model");
        form.Add(new StringContent("json"), "response_format");
        if (!string.IsNullOrWhiteSpace(language)) form.Add(new StringContent(language), "language");

        using var request = Request(endpoint, "audio/transcriptions", form);
        using var response = await SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
                ? text.GetString() ?? string.Empty
                : throw new SpeechServiceException("The speech service answered without any text.");
        }
        catch (JsonException)
        {
            // Some servers ignore response_format and send plain text.
            return body;
        }
    }

    public async Task<SpeechAudio> SynthesizeAsync(SpeechEndpoint endpoint, string model, string voice, string text, CancellationToken ct = default)
    {
        // A string body rather than JsonContent, for the same reason: it has a Content-Length.
        var body = JsonSerializer.Serialize(new { model, input = text, voice, response_format = "mp3" });
        using var request = Request(endpoint, "audio/speech", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        using var response = await SendAsync(request, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length == 0) throw new SpeechServiceException("The speech service sent no audio.");
        return new SpeechAudio(bytes, response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg");
    }

    private static HttpRequestMessage Request(SpeechEndpoint endpoint, string path, HttpContent content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint.BaseUrl.TrimEnd('/')}/{path}") { Content = content };
        if (!string.IsNullOrWhiteSpace(endpoint.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new SpeechServiceException($"Could not reach the speech service at {request.RequestUri?.GetLeftPart(UriPartial.Authority)}: {ex.Message}", ex);
        }

        if (response.IsSuccessStatusCode) return response;

        var detail = await ErrorOf(response, ct);
        response.Dispose();
        throw new SpeechServiceException(response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "The speech service refused the key.",
            HttpStatusCode.NotFound => $"The speech service has no {request.RequestUri?.AbsolutePath} (check the address ends in /v1, and the model name).{detail}",
            _ => $"The speech service failed ({(int)response.StatusCode}).{detail}",
        });
    }

    /// <summary>" It said: …" from an error body, kept short; nothing when there is none.</summary>
    private static async Task<string> ErrorOf(HttpResponseMessage response, CancellationToken ct)
    {
        var body = (await response.Content.ReadAsStringAsync(ct)).Trim();
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                body = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message)
                    ? message.GetString() ?? body
                    : error.ToString();
            }
            else if (root.TryGetProperty("detail", out var detail))
            {
                body = detail.ToString();
            }
        }
        catch (JsonException)
        {
            // Plain text: use it as it is.
        }

        if (body.Length == 0) return string.Empty;
        return $" It said: {(body.Length > 200 ? body[..200] + "…" : body)}";
    }

    private static string ContentTypeOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".wav" => "audio/wav",
        ".mp3" => "audio/mpeg",
        ".m4a" or ".mp4" or ".aac" => "audio/mp4",
        ".ogg" or ".oga" or ".opus" => "audio/ogg",
        ".webm" => "audio/webm",
        ".flac" => "audio/flac",
        _ => "application/octet-stream",
    };
}
