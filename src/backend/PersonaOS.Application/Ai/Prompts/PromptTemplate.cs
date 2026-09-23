using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PersonaOS.Application.Ai.Prompts;

/// <summary>
/// One prompt fragment, parsed from a <c>.prompty</c> file: YAML-style front matter between
/// <c>---</c> lines, then a body in a small subset of Mustache.
///
/// <list type="bullet">
/// <item><c>{{name}}</c> inserts a value.</item>
/// <item><c>{{#name}}…{{/name}}</c> renders its content when the value is set; for a list, once
/// per item, with the item as <c>{{.}}</c>.</item>
/// <item><c>{{^name}}…{{/name}}</c> renders its content when the value is empty or missing.</item>
/// <item><c>{{! comment }}</c> is dropped.</item>
/// </list>
///
/// A section or comment tag alone on its line takes the line with it, so templates can be laid out
/// one tag per line without leaving blank lines in the prompt. Values are inserted as they are,
/// never re-parsed, so text the user wrote (a persona containing braces, say) cannot inject tags.
///
/// Deliberately strict: an unknown variable or an unbalanced section throws
/// <see cref="PromptTemplateException"/> rather than rendering something half right, which lets
/// <see cref="PromptLibrary"/> fall back to the shipped default when a hand-edited override breaks.
/// </summary>
public sealed partial class PromptTemplate
{
    private readonly IReadOnlyList<Node> _nodes;

    private PromptTemplate(string name, string? description, IReadOnlyList<Node> nodes)
    {
        Name = name;
        Description = description;
        _nodes = nodes;
    }

    public string Name { get; }

    /// <summary>The front matter's <c>description</c>: what the fragment is for, for whoever edits it.</summary>
    public string? Description { get; }

    public static PromptTemplate Parse(string name, string source)
    {
        var text = source.Replace("\r\n", "\n");
        var (frontMatter, body) = SplitFrontMatter(name, text);
        frontMatter.TryGetValue("description", out var description);
        return new PromptTemplate(name, description, ParseBody(name, body));
    }

    public string Render(IReadOnlyDictionary<string, object?> values)
    {
        var output = new StringBuilder();
        RenderNodes(_nodes, new Scope(values, Item: null, Parent: null), output);
        return output.ToString();
    }

    private static (Dictionary<string, string> FrontMatter, string Body) SplitFrontMatter(string name, string text)
    {
        var frontMatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!text.StartsWith("---\n", StringComparison.Ordinal)) return (frontMatter, text);

        var close = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (close < 0) throw new PromptTemplateException(name, "front matter opened with --- is never closed");

        foreach (var line in text[4..close].Split('\n'))
        {
            // Top-level "key: value" only; nested YAML (inputs lists and the like) is for the
            // reader and is ignored here.
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line.StartsWith('#')) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            frontMatter[line[..colon].Trim()] = line[(colon + 1)..].Trim().Trim('"', '\'');
        }

        var bodyStart = text.IndexOf('\n', close + 4);
        return (frontMatter, bodyStart < 0 ? string.Empty : text[(bodyStart + 1)..]);
    }

    private static List<Node> ParseBody(string name, string body)
    {
        var root = new List<Node>();
        var current = root;
        var open = new Stack<(SectionNode Section, List<Node> Parent)>();
        var position = 0;

        foreach (Match tag in TagPattern().Matches(body))
        {
            var kind = tag.Groups["kind"].Value;
            var key = tag.Groups["key"].Value;
            var textEnd = tag.Index;
            var next = tag.Index + tag.Length;

            if (kind.Length > 0 && IsStandalone(body, position, tag.Index, next, out var lineStart, out var lineNext))
            {
                textEnd = lineStart;
                next = lineNext;
            }

            if (textEnd > position) current.Add(new TextNode(body[position..textEnd]));
            position = next;

            if (kind == "!") continue;
            if (!ValidKey().IsMatch(key)) throw new PromptTemplateException(name, $"'{{{{{kind}{key}}}}}' is not a valid tag");

            switch (kind)
            {
                case "#" or "^":
                    var section = new SectionNode(key, Inverted: kind == "^", []);
                    current.Add(section);
                    open.Push((section, current));
                    current = section.Children;
                    break;
                case "/":
                    if (open.Count == 0 || open.Peek().Section.Key != key)
                        throw new PromptTemplateException(name, $"{{{{/{key}}}}} closes a section that is not open");
                    current = open.Pop().Parent;
                    break;
                default:
                    current.Add(new VariableNode(key));
                    break;
            }
        }

        if (open.Count > 0)
            throw new PromptTemplateException(name, $"{{{{#{open.Peek().Section.Key}}}}} is never closed");
        if (position < body.Length) current.Add(new TextNode(body[position..]));
        return root;
    }

    /// <summary>
    /// True when the tag is the only thing on its line (whitespace aside). Then the whole line,
    /// newline included, belongs to the tag.
    /// </summary>
    private static bool IsStandalone(string body, int position, int tagStart, int tagEnd, out int lineStart, out int lineNext)
    {
        lineStart = tagStart == 0 ? 0 : body.LastIndexOf('\n', tagStart - 1) + 1;
        var lineEnd = body.IndexOf('\n', tagEnd);
        if (lineEnd < 0) lineEnd = body.Length;
        lineNext = Math.Min(lineEnd + 1, body.Length);

        return lineStart >= position
            && string.IsNullOrWhiteSpace(body[lineStart..tagStart])
            && string.IsNullOrWhiteSpace(body[tagEnd..lineEnd]);
    }

    private void RenderNodes(IEnumerable<Node> nodes, Scope scope, StringBuilder output)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode text:
                    output.Append(text.Text);
                    break;
                case VariableNode variable:
                    output.Append(Format(Lookup(variable.Key, scope)));
                    break;
                case SectionNode { Inverted: true } section:
                    if (!IsSet(Lookup(section.Key, scope))) RenderNodes(section.Children, scope, output);
                    break;
                case SectionNode section:
                    var value = Lookup(section.Key, scope);
                    if (!IsSet(value)) break;
                    if (value is IEnumerable items and not string)
                    {
                        foreach (var item in items) RenderNodes(section.Children, scope with { Item = item, Parent = scope }, output);
                    }
                    else
                    {
                        RenderNodes(section.Children, scope with { Item = value, Parent = scope }, output);
                    }
                    break;
            }
        }
    }

    private object? Lookup(string key, Scope scope)
    {
        if (key == ".")
        {
            return scope.Parent is not null
                ? scope.Item
                : throw new PromptTemplateException(Name, "{{.}} is only meaningful inside a section");
        }

        return scope.Values.TryGetValue(key, out var value)
            ? value
            : throw new PromptTemplateException(Name, $"unknown variable '{key}'");
    }

    private static bool IsSet(object? value) => value switch
    {
        null => false,
        bool flag => flag,
        string text => text.Length > 0,
        IEnumerable items => items.GetEnumerator().MoveNext(),
        _ => true,
    };

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    [GeneratedRegex(@"\{\{\s*(?<kind>[#^/!]?)\s*(?<key>.*?)\s*\}\}", RegexOptions.Singleline)]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"^(\.|[A-Za-z][A-Za-z0-9_]*)$")]
    private static partial Regex ValidKey();

    private abstract record Node;

    private sealed record TextNode(string Text) : Node;

    private sealed record VariableNode(string Key) : Node;

    private sealed record SectionNode(string Key, bool Inverted, List<Node> Children) : Node;

    /// <summary>Variables are always read from the root values; sections only add <c>{{.}}</c>.</summary>
    private sealed record Scope(IReadOnlyDictionary<string, object?> Values, object? Item, Scope? Parent);
}

public sealed class PromptTemplateException(string template, string problem)
    : Exception($"Prompt template '{template}': {problem}.")
{
    public string Template { get; } = template;
}
