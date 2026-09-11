using System.Text.Json;
using PersonaOS.Application.Ai.Tools;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Documents.Tools;

/// <summary>Shared plumbing for the document tools.</summary>
public abstract class DocumentToolBase : IPersonaTool
{
    protected static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string InputSchemaJson { get; }
    /// <summary>Writes by default; read-only tools override this to false.</summary>
    public virtual bool Mutates => true;
    public string? RequiredFeature => InstanceConfig.Modules.Docs;

    public abstract Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default);

    protected static string? GetString(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    protected static int? GetInt(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    protected static string Ok(object payload) => JsonSerializer.Serialize(payload, JsonOpts);
}

public class ListDocumentsTool(IDocumentService documents) : DocumentToolBase
{
    public override string Name => "list_documents";
    public override bool Mutates => false;
    public override string Description =>
        "Lists the files the user has uploaded, with their id, name, type, and description. " +
        "Use this to find a document before reading it.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "search": { "type": "string", "description": "Optional filter on file name or description." }
          }
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default) =>
        Ok(new { documents = await documents.ListAsync(GetString(input, "search"), ct) });
}

public class ReadDocumentTool(IDocumentService documents) : DocumentToolBase
{
    public override string Name => "read_document";
    public override bool Mutates => false;
    public override string Description =>
        "Reads the text of an uploaded document so you can answer questions about it. " +
        "Only plain-text formats (txt, md, csv, json, xml, code) can be read; PDFs, images, " +
        "and Office files cannot. Long documents are truncated. " +
        "Find the id with list_documents, or pass the file name.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "documentId": { "type": "integer", "description": "Document id from list_documents." },
            "fileName": { "type": "string", "description": "Alternative to documentId: the document's file name." }
          }
        }
        """;

    public override async Task<string> ExecuteAsync(JsonElement input, CancellationToken ct = default)
    {
        var id = GetInt(input, "documentId");

        if (id is null)
        {
            var fileName = GetString(input, "fileName")
                ?? throw new DocumentValidationException("Provide either documentId or fileName.");

            var matches = await documents.ListAsync(fileName, ct);
            var exact = matches.FirstOrDefault(d =>
                string.Equals(d.FileName, fileName, StringComparison.OrdinalIgnoreCase));

            if (exact is not null) id = exact.Id;
            else if (matches.Count == 1) id = matches[0].Id;
            else if (matches.Count == 0)
                throw new DocumentValidationException($"No document matches '{fileName}'.");
            else
                throw new DocumentValidationException(
                    $"Several documents match '{fileName}': " +
                    string.Join(", ", matches.Select(m => $"{m.Id}={m.FileName}")) +
                    ". Retry with documentId.");
        }

        var document = await documents.GetAsync(id.Value, ct)
            ?? throw new DocumentValidationException($"Document {id} does not exist.");

        return Ok(new
        {
            id = document.Id,
            fileName = document.FileName,
            content = await documents.ReadAsTextAsync(id.Value, ct),
        });
    }
}
