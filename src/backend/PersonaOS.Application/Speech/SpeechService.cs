using Microsoft.Extensions.DependencyInjection;
using PersonaOS.Application.Common.Exceptions;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Application.Configuration;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Speech;

/// <summary>What the clients may know about the speech service: never the key, only whether one is set.</summary>
public record SpeechStatus(
    bool SpeechToText,
    bool TextToSpeech,
    string? BaseUrl,
    string? SttModel,
    string? TtsModel,
    string? TtsVoice,
    bool HasApiKey);

/// <summary>New speech settings. Null leaves a field as it is; an empty string clears it.</summary>
public record SpeechSettings(string? BaseUrl, string? SttModel, string? TtsModel, string? TtsVoice, string? ApiKey);

public class SpeechValidationException(string message) : DomainValidationException(message, "speech_invalid");

/// <summary>Speech was asked for while no service is set up for it (409, not a failure of the service).</summary>
public class SpeechNotConfiguredException(string message) : Exception(message);

public interface ISpeechService
{
    Task<SpeechStatus> GetStatusAsync(CancellationToken ct = default);

    Task<SpeechStatus> UpdateAsync(SpeechSettings settings, CancellationToken ct = default);

    Task<string> TranscribeAsync(Stream audio, string fileName, string? language, CancellationToken ct = default);

    Task<SpeechAudio> SpeakAsync(string text, CancellationToken ct = default);

    /// <summary>Tries each configured direction once, without saving anything. Returns what worked or why not.</summary>
    Task<(bool Ok, string Message)> TestAsync(SpeechSettings? unsaved, CancellationToken ct = default);
}

/// <summary>
/// The optional speech service: speech-to-text and text-to-speech run by a server the owner points
/// at (faster-whisper, Kokoro, OpenAI…). The API orchestrates: the phone only ever talks to
/// PersonaOS, which calls the service with the stored, encrypted key. Nothing here is required:
/// without it the phone uses its own speech engine.
/// </summary>
public class SpeechService(
    IInstanceConfigService configService,
    [FromKeyedServices(SecretPurposes.SpeechApiKey)] ISecretProtector protector,
    ISpeechToText stt,
    ITextToSpeech tts) : ISpeechService
{
    /// <summary>What a reply read aloud may hold at most: a long answer is cut rather than refused.</summary>
    public const int MaxSpokenCharacters = 4_000;

    /// <summary>The voice used when none is set; OpenAI's default name, which several servers accept.</summary>
    public const string DefaultVoice = "alloy";

    public async Task<SpeechStatus> GetStatusAsync(CancellationToken ct = default) =>
        ToStatus(await configService.GetOrCreateAsync(ct));

    public async Task<SpeechStatus> UpdateAsync(SpeechSettings settings, CancellationToken ct = default)
    {
        var baseUrl = settings.BaseUrl is null ? null : NormalizeUrl(settings.BaseUrl);
        await configService.UpdateAsync(c =>
        {
            if (settings.BaseUrl is not null) c.SpeechBaseUrl = baseUrl;
            if (settings.SttModel is not null) c.SpeechSttModel = Blank(settings.SttModel);
            if (settings.TtsModel is not null) c.SpeechTtsModel = Blank(settings.TtsModel);
            if (settings.TtsVoice is not null) c.SpeechTtsVoice = Blank(settings.TtsVoice);
            if (settings.ApiKey is not null)
                c.SpeechApiKeyEncrypted = string.IsNullOrWhiteSpace(settings.ApiKey) ? null : protector.Protect(settings.ApiKey.Trim());
        }, ct);
        return await GetStatusAsync(ct);
    }

    public async Task<string> TranscribeAsync(Stream audio, string fileName, string? language, CancellationToken ct = default)
    {
        var config = await configService.GetOrCreateAsync(ct);
        if (!ToStatus(config).SpeechToText)
            throw new SpeechNotConfiguredException("No speech-to-text service is set up. Add one in Settings, or use the phone's own.");
        var text = await stt.TranscribeAsync(Endpoint(config), config.SpeechSttModel!, audio, fileName, Blank(language), ct);
        return text.Trim();
    }

    public async Task<SpeechAudio> SpeakAsync(string text, CancellationToken ct = default)
    {
        var config = await configService.GetOrCreateAsync(ct);
        if (!ToStatus(config).TextToSpeech)
            throw new SpeechNotConfiguredException("No text-to-speech service is set up. Add one in Settings, or use the phone's own.");
        var spoken = (text ?? string.Empty).Trim();
        if (spoken.Length == 0) throw new SpeechValidationException("There is nothing to say.");
        if (spoken.Length > MaxSpokenCharacters) spoken = spoken[..MaxSpokenCharacters];
        return await tts.SynthesizeAsync(Endpoint(config), config.SpeechTtsModel!, config.SpeechTtsVoice ?? DefaultVoice, spoken, ct);
    }

    public async Task<(bool Ok, string Message)> TestAsync(SpeechSettings? unsaved, CancellationToken ct = default)
    {
        var config = await configService.GetOrCreateAsync(ct);
        var baseUrl = unsaved?.BaseUrl is { } url ? NormalizeUrl(url) : config.SpeechBaseUrl;
        if (baseUrl is null) return (false, "Set the speech service's address first.");

        var key = !string.IsNullOrWhiteSpace(unsaved?.ApiKey) ? unsaved.ApiKey.Trim() : StoredKey(config);
        var endpoint = new SpeechEndpoint(baseUrl, key);
        var sttModel = unsaved?.SttModel is { } s ? Blank(s) : config.SpeechSttModel;
        var ttsModel = unsaved?.TtsModel is { } t ? Blank(t) : config.SpeechTtsModel;
        var voice = (unsaved?.TtsVoice is { } v ? Blank(v) : config.SpeechTtsVoice) ?? DefaultVoice;
        if (sttModel is null && ttsModel is null) return (false, "Set a speech-to-text model, a text-to-speech model, or both.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var worked = new List<string>();
        try
        {
            if (ttsModel is not null)
            {
                var audio = await tts.SynthesizeAsync(endpoint, ttsModel, voice, "Speech is working.", timeout.Token);
                worked.Add($"text-to-speech ({ttsModel}, {audio.Bytes.Length / 1024} KB of audio)");
            }
            if (sttModel is not null)
            {
                using var silence = new MemoryStream(SilentWav());
                await stt.TranscribeAsync(endpoint, sttModel, silence, "test.wav", "en", timeout.Token);
                worked.Add($"speech-to-text ({sttModel})");
            }
        }
        catch (SpeechServiceException ex)
        {
            return (false, ex.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, "The speech service did not answer within 60 seconds.");
        }

        return (true, $"Connected — {string.Join(" and ", worked)} via {baseUrl}.");
    }

    private SpeechEndpoint Endpoint(InstanceConfig config) => new(config.SpeechBaseUrl!, StoredKey(config));

    private string? StoredKey(InstanceConfig config) =>
        string.IsNullOrEmpty(config.SpeechApiKeyEncrypted) ? null : protector.Unprotect(config.SpeechApiKeyEncrypted);

    private static SpeechStatus ToStatus(InstanceConfig c) => new(
        SpeechToText: c.SpeechBaseUrl is not null && c.SpeechSttModel is not null,
        TextToSpeech: c.SpeechBaseUrl is not null && c.SpeechTtsModel is not null,
        c.SpeechBaseUrl, c.SpeechSttModel, c.SpeechTtsModel, c.SpeechTtsVoice,
        HasApiKey: !string.IsNullOrEmpty(c.SpeechApiKeyEncrypted));

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>An http(s) address without a trailing slash; empty clears it.</summary>
    private static string? NormalizeUrl(string url)
    {
        var value = url.Trim().TrimEnd('/');
        if (value.Length == 0) return null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new SpeechValidationException("The speech service address must start with http:// or https://, e.g. http://localhost:8000/v1.");
        return value;
    }

    /// <summary>Half a second of 16 kHz mono silence: enough for a service to accept, too little to cost anything.</summary>
    private static byte[] SilentWav()
    {
        const int sampleRate = 16_000;
        const int samples = sampleRate / 2;
        var data = samples * 2;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sampleRate); w.Write(sampleRate * 2);
        w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(data); w.Write(new byte[data]);
        w.Flush();
        return ms.ToArray();
    }
}
