using System.Net;
using System.Text;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Speech;
using PersonaOS.Infrastructure.Speech;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Speech;

/// <summary>Records what the speech service was asked, and answers from a script.</summary>
internal sealed class FakeSpeech : ISpeechToText, ITextToSpeech
{
    public List<(SpeechEndpoint Endpoint, string Model, string FileName, string? Language, long Bytes)> Transcribed { get; } = [];
    public List<(SpeechEndpoint Endpoint, string Model, string Voice, string Text)> Spoken { get; } = [];
    public string Transcript { get; set; } = " Remind me to call mum ";
    public Exception? Failure { get; set; }

    public Task<string> TranscribeAsync(SpeechEndpoint endpoint, string model, Stream audio, string fileName, string? language, CancellationToken ct = default)
    {
        if (Failure is not null) throw Failure;
        using var copy = new MemoryStream();
        audio.CopyTo(copy);
        Transcribed.Add((endpoint, model, fileName, language, copy.Length));
        return Task.FromResult(Transcript);
    }

    public Task<SpeechAudio> SynthesizeAsync(SpeechEndpoint endpoint, string model, string voice, string text, CancellationToken ct = default)
    {
        if (Failure is not null) throw Failure;
        Spoken.Add((endpoint, model, voice, text));
        return Task.FromResult(new SpeechAudio([1, 2, 3], "audio/mpeg"));
    }
}

public class SpeechServiceTests
{
    private static (SpeechService Service, FakeSpeech Speech, TestDbContext Db) Setup()
    {
        var db = TestDbContext.Create();
        var speech = new FakeSpeech();
        return (new SpeechService(new FakeInstanceConfigService(db), new FakeSecretProtector(), speech, speech), speech, db);
    }

    [Fact]
    public async Task Without_a_service_neither_direction_is_on_and_asking_says_so()
    {
        var (service, _, _) = Setup();

        var status = await service.GetStatusAsync();

        Assert.False(status.SpeechToText);
        Assert.False(status.TextToSpeech);
        await Assert.ThrowsAsync<SpeechNotConfiguredException>(() => service.SpeakAsync("hello"));
        await Assert.ThrowsAsync<SpeechNotConfiguredException>(() => service.TranscribeAsync(new MemoryStream([1]), "a.m4a", null));
    }

    [Fact]
    public async Task Each_direction_is_on_once_its_model_is_set()
    {
        var (service, _, _) = Setup();

        var status = await service.UpdateAsync(new SpeechSettings("http://speech:8000/v1/", "whisper-1", null, null, null));

        Assert.True(status.SpeechToText);
        Assert.False(status.TextToSpeech);
        Assert.Equal("http://speech:8000/v1", status.BaseUrl);
    }

    [Fact]
    public async Task The_key_is_stored_encrypted_sent_to_the_service_and_never_shown()
    {
        var (service, speech, db) = Setup();
        await service.UpdateAsync(new SpeechSettings("https://api.openai.com/v1", "whisper-1", "tts-1", null, "sk-speech"));

        var status = await service.GetStatusAsync();
        await service.SpeakAsync("Hello");

        Assert.True(status.HasApiKey);
        Assert.NotEqual("sk-speech", db.InstanceConfig.Single().SpeechApiKeyEncrypted);
        Assert.Equal("sk-speech", speech.Spoken.Single().Endpoint.ApiKey);
        Assert.Equal(SpeechService.DefaultVoice, speech.Spoken.Single().Voice);
    }

    [Fact]
    public async Task A_transcript_comes_back_trimmed_with_the_file_passed_through()
    {
        var (service, speech, _) = Setup();
        await service.UpdateAsync(new SpeechSettings("http://speech/v1", "Systran/faster-whisper-small", null, null, null));

        var text = await service.TranscribeAsync(new MemoryStream(new byte[100]), "speech.m4a", "en");

        Assert.Equal("Remind me to call mum", text);
        var call = speech.Transcribed.Single();
        Assert.Equal(("Systran/faster-whisper-small", "speech.m4a", "en", 100L), (call.Model, call.FileName, call.Language, call.Bytes));
        Assert.Null(call.Endpoint.ApiKey);
    }

    [Fact]
    public async Task A_long_reply_is_cut_rather_than_refused()
    {
        var (service, speech, _) = Setup();
        await service.UpdateAsync(new SpeechSettings("http://speech/v1", null, "kokoro", "af_heart", null));

        await service.SpeakAsync(new string('a', SpeechService.MaxSpokenCharacters + 50));

        Assert.Equal(SpeechService.MaxSpokenCharacters, speech.Spoken.Single().Text.Length);
        Assert.Equal("af_heart", speech.Spoken.Single().Voice);
    }

    [Fact]
    public async Task An_address_that_is_not_http_is_refused()
    {
        var (service, _, _) = Setup();

        await Assert.ThrowsAsync<SpeechValidationException>(() => service.UpdateAsync(new SpeechSettings("speech:8000", "whisper-1", null, null, null)));
    }

    [Fact]
    public async Task Clearing_the_address_turns_speech_off()
    {
        var (service, _, _) = Setup();
        await service.UpdateAsync(new SpeechSettings("http://speech/v1", "whisper-1", "tts-1", null, null));

        var status = await service.UpdateAsync(new SpeechSettings("", null, null, null, null));

        Assert.False(status.SpeechToText);
        Assert.False(status.TextToSpeech);
    }

    [Fact]
    public async Task The_test_tries_each_direction_without_saving()
    {
        var (service, speech, db) = Setup();

        var (ok, message) = await service.TestAsync(new SpeechSettings("http://speech/v1", "whisper-1", "tts-1", "nova", null));

        Assert.True(ok);
        Assert.Contains("text-to-speech (tts-1", message);
        Assert.Contains("speech-to-text (whisper-1)", message);
        Assert.Single(speech.Spoken);
        Assert.True(speech.Transcribed.Single().Bytes > 44); // a real, if silent, WAV
        Assert.Null(db.InstanceConfig.Single().SpeechBaseUrl);
    }

    [Fact]
    public async Task A_failing_service_is_reported_by_the_test_not_thrown()
    {
        var (service, speech, _) = Setup();
        speech.Failure = new SpeechServiceException("The speech service refused the key.");

        var (ok, message) = await service.TestAsync(new SpeechSettings("http://speech/v1", "whisper-1", null, null, null));

        Assert.False(ok);
        Assert.Equal("The speech service refused the key.", message);
    }
}

public class OpenAiSpeechClientTests
{
    private sealed class Stub(HttpStatusCode status, string body, string contentType = "application/json") : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, contentType) };
            return response;
        }
    }

    [Fact]
    public async Task Transcribes_through_the_audio_transcriptions_endpoint_with_the_key()
    {
        var stub = new Stub(HttpStatusCode.OK, """{"text":"hello there"}""");
        var client = new OpenAiSpeechClient(new HttpClient(stub));

        var text = await client.TranscribeAsync(new SpeechEndpoint("http://speech/v1/", "sk-1"), "whisper-1", new MemoryStream([1, 2]), "a.m4a", "en");

        Assert.Equal("hello there", text);
        Assert.Equal("http://speech/v1/audio/transcriptions", stub.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer sk-1", stub.Request.Headers.Authorization!.ToString());
        Assert.Contains("whisper-1", stub.RequestBody);
        Assert.Contains("name=language", stub.RequestBody);
    }

    [Fact]
    public async Task Synthesizes_through_the_audio_speech_endpoint_without_a_key_when_there_is_none()
    {
        var stub = new Stub(HttpStatusCode.OK, "MP3DATA", "audio/mpeg");
        var client = new OpenAiSpeechClient(new HttpClient(stub));

        var audio = await client.SynthesizeAsync(new SpeechEndpoint("http://kokoro:8880/v1", null), "kokoro", "af_heart", "Hi");

        Assert.Equal("http://kokoro:8880/v1/audio/speech", stub.Request!.RequestUri!.ToString());
        Assert.Null(stub.Request.Headers.Authorization);
        Assert.Contains("\"voice\":\"af_heart\"", stub.RequestBody);
        Assert.Equal("audio/mpeg", audio.ContentType);
        Assert.Equal("MP3DATA", Encoding.UTF8.GetString(audio.Bytes));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":{"message":"bad key"}}""", "refused the key")]
    [InlineData(HttpStatusCode.NotFound, """{"detail":"Not Found"}""", "check the address ends in /v1")]
    [InlineData(HttpStatusCode.InternalServerError, """{"error":{"message":"model not loaded"}}""", "It said: model not loaded")]
    public async Task A_failure_says_why_in_words(HttpStatusCode status, string body, string expected)
    {
        var client = new OpenAiSpeechClient(new HttpClient(new Stub(status, body)));

        var ex = await Assert.ThrowsAsync<SpeechServiceException>(
            () => client.SynthesizeAsync(new SpeechEndpoint("http://speech/v1", null), "tts-1", "alloy", "Hi"));

        Assert.Contains(expected, ex.Message);
    }
}
