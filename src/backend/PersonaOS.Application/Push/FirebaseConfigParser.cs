using System.Text.Json;
using PersonaOS.Application.Common.Exceptions;

namespace PersonaOS.Application.Push;

/// <summary>Invalid push configuration; the message is shown to the admin as-is.</summary>
public class PushConfigValidationException(string message)
    : DomainValidationException(message, "push_config_validation_failed");

/// <summary>
/// The Firebase options the mobile app initialises Firebase with at runtime, in place of a
/// google-services.json compiled into the APK. Public identifiers, not secrets.
/// </summary>
public record FcmClientOptions(string ApiKey, string AppId, string MessagingSenderId, string ProjectId);

/// <summary>
/// Reads the two files an admin uploads to turn push on, and refuses combinations that would
/// only fail later.
///
/// Everything checked here is something that otherwise surfaces as a silent non-delivery days
/// afterwards: a key from one Firebase project and an app from another gets rejected by FCM as
/// <c>SenderIdMismatch</c> on every send, and a google-services.json registered for a different
/// package name gives the app nothing to initialise with. Catching it at upload turns "push
/// just doesn't work" into a message the admin can act on.
/// </summary>
public static class FirebaseConfigParser
{
    /// <summary>The Android package every PersonaOS install ships under.</summary>
    public const string AndroidPackageName = "com.personaos.personaos_mobile";

    /// <summary>
    /// Validates a service-account key and returns its project id. The key itself is returned
    /// untouched by the caller — it is encrypted whole, never picked apart and stored in pieces.
    /// </summary>
    public static string ReadServiceAccountProjectId(string serviceAccountJson)
    {
        using var doc = Parse(serviceAccountJson, "The service account key");
        var root = doc.RootElement;

        // A google-services.json uploaded in the wrong slot is the likeliest mistake, and both
        // are JSON from the same console — say which file this is, rather than just "invalid".
        if (root.TryGetProperty("project_info", out _))
            throw new PushConfigValidationException(
                "That looks like google-services.json. The service account key is the file from " +
                "Project settings → Service accounts → Generate new private key.");

        if (String(root, "type") != "service_account")
            throw new PushConfigValidationException(
                "The service account key must be a Firebase service account JSON (\"type\": \"service_account\").");

        foreach (var required in new[] { "project_id", "private_key", "client_email" })
        {
            if (string.IsNullOrWhiteSpace(String(root, required)))
                throw new PushConfigValidationException($"The service account key is missing \"{required}\".");
        }

        return String(root, "project_id")!;
    }

    /// <summary>Extracts the Android client options for PersonaOS from a google-services.json.</summary>
    public static FcmClientOptions ReadClientOptions(string googleServicesJson)
    {
        using var doc = Parse(googleServicesJson, "google-services.json");
        var root = doc.RootElement;

        if (String(root, "type") == "service_account")
            throw new PushConfigValidationException(
                "That looks like the service account key. google-services.json is the file from " +
                "Project settings → General → Your apps → Android app.");

        if (!root.TryGetProperty("project_info", out var projectInfo))
            throw new PushConfigValidationException("google-services.json is missing \"project_info\".");

        var projectId = String(projectInfo, "project_id");
        var senderId = String(projectInfo, "project_number");
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(senderId))
            throw new PushConfigValidationException(
                "google-services.json is missing the project id or project number.");

        if (!root.TryGetProperty("client", out var clients) || clients.ValueKind != JsonValueKind.Array)
            throw new PushConfigValidationException("google-services.json lists no apps.");

        // One Firebase project can hold several apps; take only the one for this package.
        foreach (var client in clients.EnumerateArray())
        {
            if (!client.TryGetProperty("client_info", out var clientInfo)) continue;
            if (!clientInfo.TryGetProperty("android_client_info", out var android)) continue;
            if (String(android, "package_name") != AndroidPackageName) continue;

            var appId = String(clientInfo, "mobilesdk_app_id");
            var apiKey = client.TryGetProperty("api_key", out var keys)
                         && keys.ValueKind == JsonValueKind.Array
                         && keys.GetArrayLength() > 0
                ? String(keys[0], "current_key")
                : null;

            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(apiKey))
                throw new PushConfigValidationException(
                    "The PersonaOS app entry in google-services.json is missing its app id or API key.");

            return new FcmClientOptions(apiKey, appId, senderId, projectId);
        }

        throw new PushConfigValidationException(
            $"google-services.json has no Android app with package name \"{AndroidPackageName}\". " +
            "Register the Android app in Firebase with exactly that package name, then download the file again.");
    }

    /// <summary>Both files must come from the same Firebase project, or every send fails.</summary>
    public static void EnsureSameProject(string serviceAccountProjectId, FcmClientOptions client)
    {
        if (!string.Equals(serviceAccountProjectId, client.ProjectId, StringComparison.Ordinal))
            throw new PushConfigValidationException(
                $"The two files come from different Firebase projects (\"{serviceAccountProjectId}\" and " +
                $"\"{client.ProjectId}\"). Download both from the same project.");
    }

    private static JsonDocument Parse(string json, string what)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new PushConfigValidationException($"{what} is empty.");

        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                doc.Dispose();
                throw new PushConfigValidationException($"{what} is not a JSON object.");
            }
            return doc;
        }
        catch (JsonException)
        {
            throw new PushConfigValidationException($"{what} is not valid JSON.");
        }
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
