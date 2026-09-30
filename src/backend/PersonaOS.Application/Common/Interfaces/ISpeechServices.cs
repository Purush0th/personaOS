namespace PersonaOS.Application.Common.Interfaces;

/// <summary>Where a speech service lives: its base URL (".../v1") and, when it needs one, its key.</summary>
public record SpeechEndpoint(string BaseUrl, string? ApiKey);

/// <summary>Spoken audio as the service returned it.</summary>
public record SpeechAudio(byte[] Bytes, string ContentType);

/// <summary>A speech service failed or could not be reached; the message is written for the user.</summary>
public class SpeechServiceException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Speech-to-text port. The phone sends its recording to the PersonaOS API, which passes it here;
/// the adapter speaks the OpenAI-style audio API (<c>/v1/audio/transcriptions</c>) that
/// faster-whisper servers and OpenAI itself offer. Independent of the chat model.
/// </summary>
public interface ISpeechToText
{
    /// <param name="language">An ISO-639-1 hint such as "en"; null lets the service detect it.</param>
    Task<string> TranscribeAsync(
        SpeechEndpoint endpoint, string model, Stream audio, string fileName, string? language, CancellationToken ct = default);
}

/// <summary>
/// Text-to-speech port, over the OpenAI-style <c>/v1/audio/speech</c> API (Kokoro and OpenAI
/// offer it). The API fetches the audio and hands it to the phone to play.
/// </summary>
public interface ITextToSpeech
{
    Task<SpeechAudio> SynthesizeAsync(SpeechEndpoint endpoint, string model, string voice, string text, CancellationToken ct = default);
}
