using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PersonaOS.Application.Common.Exceptions;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Application.Memories;

/// <summary>A memory as the clients and the assistant see it.</summary>
/// <param name="SourceConversationId">The conversation's address (<c>/chat/&lt;id&gt;</c>), when it came from chat.</param>
public record MemoryDto(
    int Id,
    string Content,
    string Category,
    string? SourceConversationId,
    string? SourceConversationTitle,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public class MemoryValidationException(string message) : DomainValidationException(message, "memory_invalid");

public class MemoryNotFoundException(int id) : DomainValidationException($"There is no memory {id}. Use an id from search_memories.", "memory_not_found");

public interface IMemoryService
{
    /// <summary>Every memory, newest first, optionally narrowed by words and a category.</summary>
    Task<IReadOnlyList<MemoryDto>> ListAsync(string? search = null, string? category = null, CancellationToken ct = default);

    Task<MemoryDto?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Saves a memory. One that says the same as an existing one updates that one instead.</summary>
    Task<MemoryDto> CreateAsync(string content, string? category, int? sourceConversationId, CancellationToken ct = default);

    /// <summary>Changes what is given; null leaves a field as it is.</summary>
    Task<MemoryDto> UpdateAsync(int id, string? content, string? category, CancellationToken ct = default);

    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// The memories worth putting in front of the model for this request, most useful first and
    /// bounded: those sharing words with <paramref name="request"/>, then the latest preferences,
    /// which apply to almost any answer. Keyword matching only: no embedding service needed.
    /// </summary>
    Task<IReadOnlyList<MemoryDto>> RelevantAsync(string? request, int max, CancellationToken ct = default);
}

public partial class MemoryService(IAppDbContext db) : IMemoryService
{
    public const int MaxLength = 500;

    /// <summary>How many of the latest preferences come along with any request.</summary>
    private const int StandingPreferences = 3;

    /// <summary>Words too common to say what a request is about.</summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "you", "your", "are", "was", "were", "with", "that", "this", "what", "when",
        "where", "which", "who", "how", "why", "can", "could", "would", "should", "will", "have", "has",
        "had", "not", "but", "all", "any", "about", "from", "into", "than", "then", "them", "they", "their",
        "there", "been", "being", "does", "did", "doing", "just", "also", "some", "more", "most", "very",
        "please", "tell", "know", "want", "like", "need", "make", "get", "give", "let", "me", "my", "mine",
        "our", "out", "its", "it's", "i'm", "im", "yes", "okay", "now", "today", "tomorrow",
    };

    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex Word();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public async Task<IReadOnlyList<MemoryDto>> ListAsync(string? search = null, string? category = null, CancellationToken ct = default)
    {
        var query = Query();
        if (!string.IsNullOrWhiteSpace(category))
        {
            var wanted = NormalizeCategory(category);
            query = query.Where(m => m.Category == wanted);
        }

        var all = (await query.ToListAsync(ct)).OrderByDescending(m => m.UpdatedAtUtc).ThenByDescending(m => m.Id);
        if (string.IsNullOrWhiteSpace(search)) return all.Select(ToDto).ToList();

        // Every word of the search must appear, so "car insurance" does not list every car memory.
        var words = Words(search);
        return all.Where(m => words.All(w => Matches(m.Content, w))).Select(ToDto).ToList();
    }

    public async Task<MemoryDto?> GetAsync(int id, CancellationToken ct = default) =>
        await Query().FirstOrDefaultAsync(m => m.Id == id, ct) is { } memory ? ToDto(memory) : null;

    public async Task<MemoryDto> CreateAsync(string content, string? category, int? sourceConversationId, CancellationToken ct = default)
    {
        var text = Clean(content);
        var kind = NormalizeCategory(category);

        // The same thing said twice is one memory; saving it again only refreshes it.
        var all = await db.Memories.ToListAsync(ct);
        var same = all.FirstOrDefault(m => string.Equals(Normalize(m.Content), Normalize(text), StringComparison.OrdinalIgnoreCase));
        if (same is not null)
        {
            same.Category = kind;
            same.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return (await GetAsync(same.Id, ct))!;
        }

        var conversationId = sourceConversationId is int cid && await db.Conversations.AnyAsync(c => c.Id == cid, ct) ? cid : (int?)null;
        var memory = new Memory { Content = text, Category = kind, SourceConversationId = conversationId };
        db.Memories.Add(memory);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(memory.Id, ct))!;
    }

    public async Task<MemoryDto> UpdateAsync(int id, string? content, string? category, CancellationToken ct = default)
    {
        var memory = await db.Memories.FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw new MemoryNotFoundException(id);
        if (content is not null) memory.Content = Clean(content);
        if (category is not null) memory.Category = NormalizeCategory(category);
        memory.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var memory = await db.Memories.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (memory is null) return false;
        db.Memories.Remove(memory);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<MemoryDto>> RelevantAsync(string? request, int max, CancellationToken ct = default)
    {
        if (max <= 0) return [];
        var all = await Query().ToListAsync(ct);
        if (all.Count == 0) return [];

        var words = string.IsNullOrWhiteSpace(request) ? [] : Words(request);
        var matched = all
            .Select(m => (Memory: m, Score: words.Count(w => Matches(m.Content, w))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Memory.UpdatedAtUtc)
            .Select(x => x.Memory)
            .Take(max)
            .ToList();

        var preferences = all
            .Where(m => m.Category == MemoryCategories.Preference && !matched.Contains(m))
            .OrderByDescending(m => m.UpdatedAtUtc)
            .Take(Math.Min(StandingPreferences, max - matched.Count));

        return matched.Concat(preferences).Select(ToDto).ToList();
    }

    /// <summary>The known category a word names, or <see cref="MemoryCategories.Fact"/> when none is given.</summary>
    public static string NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return MemoryCategories.Fact;
        var value = category.Trim().ToLowerInvariant();
        // Models write the plural or a near word; say which ones exist rather than guessing further.
        value = value switch
        {
            "facts" => MemoryCategories.Fact,
            "preferences" or "pref" or "likes" => MemoryCategories.Preference,
            "projects" or "context" or "project context" => MemoryCategories.Project,
            "decisions" => MemoryCategories.Decision,
            _ => value,
        };
        return MemoryCategories.All.Contains(value)
            ? value
            : throw new MemoryValidationException($"Unknown category '{category}'. Use one of: {string.Join(", ", MemoryCategories.All)}.");
    }

    private IQueryable<Memory> Query() => db.Memories.AsNoTracking().Include(m => m.SourceConversation);

    private static string Clean(string content)
    {
        var text = Whitespace().Replace(content ?? string.Empty, " ").Trim();
        if (text.Length == 0) throw new MemoryValidationException("A memory needs some text.");
        if (text.Length > MaxLength) throw new MemoryValidationException($"Keep a memory under {MaxLength} characters: one short sentence.");
        return text;
    }

    private static string Normalize(string text) => Whitespace().Replace(text, " ").Trim().TrimEnd('.', '!');

    /// <summary>The words that say what a text is about: no stop words, nothing shorter than three letters.</summary>
    private static List<string> Words(string text) =>
        Word().Matches(text.ToLowerInvariant())
            .Select(m => m.Value.Trim('\''))
            .Where(w => w.Length >= 3 && !StopWords.Contains(w))
            .Distinct()
            .ToList();

    /// <summary>
    /// Whether a memory mentions a word, allowing a different ending: "workouts" finds "workout",
    /// "running" finds "run". Crude stemming, but it needs no dictionary.
    /// </summary>
    private static bool Matches(string content, string word)
    {
        var stem = word.Length > 5 ? word[..^2] : word.TrimEnd('s');
        if (stem.Length < 3) stem = word;
        return Word().Matches(content.ToLowerInvariant()).Any(m => m.Value.StartsWith(stem, StringComparison.Ordinal));
    }

    private static MemoryDto ToDto(Memory m) => new(
        m.Id, m.Content, m.Category,
        m.SourceConversation?.PublicId, m.SourceConversation?.Title,
        m.CreatedAtUtc, m.UpdatedAtUtc);
}
