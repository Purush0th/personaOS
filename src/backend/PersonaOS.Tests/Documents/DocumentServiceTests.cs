using System.Text;
using Microsoft.Extensions.Configuration;
using PersonaOS.Application.Documents;
using PersonaOS.Infrastructure.Storage;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Documents;

public class DocumentServiceTests
{
    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    private static (TestDbContext Db, DocumentService Documents, FakeDocumentStorage Storage) Setup()
    {
        var db = TestDbContext.Create();
        var storage = new FakeDocumentStorage();
        return (db, new DocumentService(db, storage), storage);
    }

    [Fact]
    public async Task An_upload_keeps_only_the_bare_file_name()
    {
        var (_, documents, _) = Setup();

        var doc = await documents.UploadAsync(new UploadDocumentRequest(Bytes("hello"), "../../etc/notes.txt"));

        Assert.Equal("notes.txt", doc.FileName);
        Assert.Equal(5, doc.SizeBytes);
    }

    [Fact]
    public async Task An_empty_file_is_refused_and_its_bytes_are_not_kept()
    {
        var (_, documents, storage) = Setup();

        await Assert.ThrowsAsync<DocumentValidationException>(() =>
            documents.UploadAsync(new UploadDocumentRequest(Bytes(string.Empty), "empty.txt")));
        Assert.Empty(storage.Files);
    }

    [Fact]
    public async Task Text_files_can_be_read_and_binary_ones_explain_why_not()
    {
        var (_, documents, _) = Setup();
        var notes = await documents.UploadAsync(new UploadDocumentRequest(Bytes("Buy milk"), "notes.md"));
        var photo = await documents.UploadAsync(new UploadDocumentRequest(Bytes("\u0089PNG"), "photo.png"));

        Assert.Equal("Buy milk", await documents.ReadAsTextAsync(notes.Id));
        var error = await Assert.ThrowsAsync<DocumentValidationException>(() => documents.ReadAsTextAsync(photo.Id));
        Assert.Contains("PNG", error.Message);
    }

    [Fact]
    public async Task Deleting_removes_the_row_and_the_bytes()
    {
        var (db, documents, storage) = Setup();
        var doc = await documents.UploadAsync(new UploadDocumentRequest(Bytes("x"), "a.txt"));

        Assert.True(await documents.DeleteAsync(doc.Id));

        Assert.Empty(db.Documents);
        Assert.Empty(storage.Files);
        Assert.False(await documents.DeleteAsync(doc.Id));
    }
}

/// <summary>The on-disk store: server-made names only, and nothing outside its folder.</summary>
public sealed class FileSystemDocumentStorageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("personaos-docs-").FullName;

    private FileSystemDocumentStorage Storage() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Documents:StoragePath"] = _root })
        .Build());

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Names_files_itself_and_keeps_only_a_plain_extension()
    {
        var storage = Storage();

        var name = await storage.SaveAsync(new MemoryStream([1, 2, 3]), ".TXT");
        var odd = await storage.SaveAsync(new MemoryStream([1]), "./../x");

        Assert.Matches("^[0-9a-f]{32}\\.txt$", name);
        Assert.Matches("^[0-9a-f]{32}$", odd);
        Assert.True(File.Exists(Path.Combine(_root, name)));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("..\\outside.txt")]
    [InlineData("sub/../../outside.txt")]
    public async Task Refuses_a_name_that_would_leave_its_folder(string storageName)
    {
        var storage = Storage();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => storage.OpenReadAsync(storageName));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => storage.DeleteAsync(storageName));
    }

    [Fact]
    public async Task Reading_text_stops_at_the_limit_and_says_so()
    {
        var storage = Storage();
        var name = await storage.SaveAsync(new MemoryStream(Encoding.UTF8.GetBytes(new string('a', 50))), ".txt");

        var text = await storage.ReadTextAsync(name, maxCharacters: 10);

        Assert.StartsWith(new string('a', 10) + "\n\n[truncated", text);
    }
}
