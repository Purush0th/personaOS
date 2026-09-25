using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PersonaOS.Application.Ai.Tools;

/// <summary>
/// How every tool writes its result for the model: web-style JSON without the fields a small
/// model turns into made-up or padded prose. Asked "what are my goals", qwen2.5:3b described a
/// goal whose <c>"description"</c> was null ("your nutrition plan isn't currently assigned to a
/// goal…"), and read out <c>"tasks": []</c>, <c>"commentCount": 0</c> and
/// <c>"attachmentCount": 0</c> as "Tasks: None, Comments: None, Attachments: None" for every goal.
/// So these are left out:
/// <list type="bullet">
/// <item>null fields;</item>
/// <item>empty lists inside an item (a tool's own top-level list still shows, empty or not, so
/// "no goals" stays a clear answer);</item>
/// <item>comment and attachment counts of zero, and <c>sortOrder</c>, which are for the screen.</item>
/// </list>
/// A tool whose description gives a missing field a meaning says so ("no points: unestimated").
/// </summary>
public static class ToolJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { LeaveOutNoise } },
    };

    public static string Serialize(object payload) => JsonSerializer.Serialize(payload, Options);

    private static void LeaveOutNoise(JsonTypeInfo type)
    {
        if (type.Kind != JsonTypeInfoKind.Object) return;
        var isWrapper = IsAnonymous(type.Type);

        foreach (var property in type.Properties)
        {
            if (property.Name == "sortOrder")
            {
                property.ShouldSerialize = static (_, _) => false;
            }
            else if (property.Name is "commentCount" or "attachmentCount")
            {
                property.ShouldSerialize = static (_, value) => value is int count && count > 0;
            }
            else if (!isWrapper && property.PropertyType != typeof(string)
                     && typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
            {
                property.ShouldSerialize = static (_, value) => value is not ICollection { Count: 0 };
            }
        }
    }

    /// <summary>The <c>new { goals = … }</c> a tool wraps its answer in.</summary>
    private static bool IsAnonymous(Type type) =>
        type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) && type.Name.Contains("AnonymousType");
}
