using System.Text.Json;
using PersonaOS.Application.Push;
using PersonaOS.Tests.TestSupport;
using Xunit;

namespace PersonaOS.Tests.Push;

public class FirebaseConfigParserTests
{
    [Fact]
    public void Reads_the_project_from_a_service_account_key()
    {
        Assert.Equal("persona-test", FirebaseConfigParser.ReadServiceAccountProjectId(PushFixtures.ServiceAccount()));
    }

    [Fact]
    public void Extracts_the_PersonaOS_client_options()
    {
        var options = FirebaseConfigParser.ReadClientOptions(PushFixtures.GoogleServices());

        Assert.Equal("persona-test", options.ProjectId);
        Assert.Equal("123456789012", options.MessagingSenderId);
        Assert.Equal("1:123456789012:android:abc", options.AppId);
        Assert.Equal("AIza-test-key", options.ApiKey);
    }

    [Fact]
    public void Picks_the_PersonaOS_app_when_the_project_holds_several()
    {
        // One Firebase project commonly holds more than one app. Taking the first entry would
        // hand the phone another app's id, and FCM would then deliver nothing to it.
        var json = PushFixtures.GoogleServices(extraClientFirst: true);

        Assert.Equal("1:123456789012:android:abc", FirebaseConfigParser.ReadClientOptions(json).AppId);
    }

    [Fact]
    public void Rejects_a_google_services_file_without_the_PersonaOS_package()
    {
        var ex = Assert.Throws<PushConfigValidationException>(() =>
            FirebaseConfigParser.ReadClientOptions(PushFixtures.GoogleServices(packageName: "com.example.other")));

        Assert.Contains(FirebaseConfigParser.AndroidPackageName, ex.Message);
    }

    [Fact]
    public void Names_the_mistake_when_the_files_are_swapped()
    {
        // The likeliest upload error: both files are JSON from the same console. A generic
        // "invalid file" would leave the admin guessing which slot is wrong.
        var keyInClientSlot = Assert.Throws<PushConfigValidationException>(() =>
            FirebaseConfigParser.ReadClientOptions(PushFixtures.ServiceAccount()));
        var clientInKeySlot = Assert.Throws<PushConfigValidationException>(() =>
            FirebaseConfigParser.ReadServiceAccountProjectId(PushFixtures.GoogleServices()));

        Assert.Contains("service account key", keyInClientSlot.Message);
        Assert.Contains("google-services.json", clientInKeySlot.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void Rejects_input_that_is_not_a_json_object(string input)
    {
        Assert.Throws<PushConfigValidationException>(() => FirebaseConfigParser.ReadServiceAccountProjectId(input));
    }

    [Fact]
    public void Rejects_a_key_missing_its_private_key()
    {
        var ex = Assert.Throws<PushConfigValidationException>(() =>
            FirebaseConfigParser.ReadServiceAccountProjectId(PushFixtures.ServiceAccount(includePrivateKey: false)));

        Assert.Contains("private_key", ex.Message);
    }
}

public class PushConfigServiceTests
{
    private static (PushConfigService Service, FakePushProviderConfigurator Push, TestDbContext Db) Create()
    {
        var db = TestDbContext.Create();
        var push = new FakePushProviderConfigurator();
        var service = new PushConfigService(new FakeInstanceConfigService(db), new FakeSecretProtector(), push);
        return (service, push, db);
    }

    [Fact]
    public async Task Stores_the_key_encrypted_and_turns_push_on()
    {
        var (service, push, db) = Create();

        var status = await service.SetAsync(PushFixtures.ServiceAccount(), PushFixtures.GoogleServices());

        Assert.True(status.Configured);
        Assert.Equal("persona-test", status.ProjectId);
        Assert.Equal(PushFixtures.ServiceAccount(), push.Current);

        var stored = db.InstanceConfig.Single();
        Assert.StartsWith("protected:", stored.FcmServiceAccountEncrypted);
        Assert.DoesNotContain("private_key", stored.FcmClientConfigJson!);
    }

    [Fact]
    public async Task Refuses_files_from_different_projects_and_changes_nothing()
    {
        var (service, push, db) = Create();

        await Assert.ThrowsAsync<PushConfigValidationException>(() =>
            service.SetAsync(PushFixtures.ServiceAccount(projectId: "project-a"), PushFixtures.GoogleServices(projectId: "project-b")));

        Assert.Empty(push.Applied);
        Assert.Null((await new FakeInstanceConfigService(db).GetOrCreateAsync()).FcmServiceAccountEncrypted);
    }

    [Fact]
    public async Task A_key_that_will_not_load_is_not_saved()
    {
        // The ordering this guards: saving first and applying second would leave the database
        // claiming push is on while the live sender never loaded the key.
        var (service, push, db) = Create();
        push.FailWith = new FormatException("bad private key");

        var ex = await Assert.ThrowsAsync<PushConfigValidationException>(() =>
            service.SetAsync(PushFixtures.ServiceAccount(), PushFixtures.GoogleServices()));

        Assert.Contains("could not be loaded", ex.Message);
        Assert.Null((await new FakeInstanceConfigService(db).GetOrCreateAsync()).FcmServiceAccountEncrypted);
    }

    [Fact]
    public async Task Hands_the_app_its_client_options_but_never_the_key()
    {
        var (service, _, _) = Create();
        await service.SetAsync(PushFixtures.ServiceAccount(), PushFixtures.GoogleServices());

        var options = await service.GetClientOptionsAsync();

        Assert.NotNull(options);
        Assert.DoesNotContain("PRIVATE KEY", JsonSerializer.Serialize(options));
    }

    [Fact]
    public async Task Clearing_turns_push_off_and_removes_the_credential()
    {
        var (service, push, _) = Create();
        await service.SetAsync(PushFixtures.ServiceAccount(), PushFixtures.GoogleServices());

        await service.ClearAsync();

        Assert.Null(push.Current);
        Assert.False((await service.GetStatusAsync()).Configured);
        Assert.Null(await service.GetClientOptionsAsync());
    }

    [Fact]
    public async Task Restores_push_from_storage_after_a_restart()
    {
        var (service, _, db) = Create();
        await service.SetAsync(PushFixtures.ServiceAccount(), PushFixtures.GoogleServices());

        // A fresh process: a new live sender that knows nothing until the stored key is applied.
        var restarted = new FakePushProviderConfigurator();
        await new PushConfigService(new FakeInstanceConfigService(db), new FakeSecretProtector(), restarted)
            .ApplyStoredAsync();

        Assert.Equal(PushFixtures.ServiceAccount(), restarted.Current);
    }
}

/// <summary>Minimal, structurally faithful Firebase files. The private key is not a real key.</summary>
internal static class PushFixtures
{
    public static string ServiceAccount(string projectId = "persona-test", bool includePrivateKey = true)
    {
        var fields = new Dictionary<string, string>
        {
            ["type"] = "service_account",
            ["project_id"] = projectId,
            ["client_email"] = $"firebase-adminsdk@{projectId}.iam.gserviceaccount.com",
        };
        if (includePrivateKey)
            fields["private_key"] = "-----BEGIN PRIVATE KEY-----\nnot-a-real-key\n-----END PRIVATE KEY-----\n";

        return JsonSerializer.Serialize(fields);
    }

    public static string GoogleServices(
        string projectId = "persona-test",
        string packageName = FirebaseConfigParser.AndroidPackageName,
        bool extraClientFirst = false)
    {
        object Client(string package, string appId) => new
        {
            client_info = new
            {
                mobilesdk_app_id = appId,
                android_client_info = new { package_name = package },
            },
            api_key = new[] { new { current_key = "AIza-test-key" } },
        };

        var clients = new List<object>();
        if (extraClientFirst) clients.Add(Client("com.example.companion", "1:123456789012:android:other"));
        clients.Add(Client(packageName, "1:123456789012:android:abc"));

        return JsonSerializer.Serialize(new
        {
            project_info = new { project_number = "123456789012", project_id = projectId },
            client = clients,
        });
    }
}
