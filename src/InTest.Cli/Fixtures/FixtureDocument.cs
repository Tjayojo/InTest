using System.Text.Json;
using System.Text.Json.Nodes;

namespace InTest.Cli.Fixtures;

/// <summary>
/// One fixture per operation: its path and query parameters, and its request body if it takes
/// one. Committed, hand-edited, and never overwritten by tooling once written.
/// </summary>
public sealed class FixtureDocument
{
    public required FixtureMeta Meta { get; init; }
    public SortedDictionary<string, string> Parameters { get; init; } = new(StringComparer.Ordinal);
    public JsonNode? Body { get; set; }

    private static readonly string[] ReservedNames =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7",
         "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    /// <summary>
    /// Explicit, not <see cref="Path.GetInvalidFileNameChars"/> alone: that call returns 41
    /// characters on Windows but only NUL and '/' on Unix, so a Windows dev box or CI agent
    /// cannot observe this list shrinking — only a direct assertion on the list itself can.
    /// Internal and exposed to InTest.Cli.Tests via InternalsVisibleTo for exactly that reason.
    /// </summary>
    internal static readonly char[] InvalidOperationKeyCharacters =
        ['/', '\\', '?', '*', ':', '"', '<', '>', '|'];

    /// <summary>
    /// Operation keys become fixture filenames. Synthesized keys are safe by construction, but
    /// a declared operationId is used verbatim and OpenAPI permits any string.
    /// <para>
    /// Returns false with a reason rather than throwing, because an unusable operationId is one
    /// operation InTest cannot serve — not grounds for abandoning a whole document. The caller
    /// records a skip and carries on, the same route non-JSON request bodies already take.
    /// </para>
    /// </summary>
    public static bool TryValidateOperationKey(string operationKey, out string reason)
    {
        if (string.IsNullOrWhiteSpace(operationKey))
        {
            reason = "operationId is empty.";
            return false;
        }

        const int maxLength = 200;
        if (operationKey.Length > maxLength)
        {
            reason = $"operationId '{operationKey}' is {operationKey.Length} characters long, which " +
                     $"exceeds the {maxLength}-character limit InTest enforces for fixture filenames — " +
                     "past that length a write can fail with a raw OS path-length error far from this " +
                     "check. Change the operationId in the OpenAPI document.";
            return false;
        }

        var invalid = InvalidOperationKeyCharacters.Concat(Path.GetInvalidFileNameChars()).ToHashSet();

        // Control characters: on Unix, Path.GetInvalidFileNameChars() returns only NUL and '/', so a
        // literal tab or other control character would otherwise pass there — the same gap already
        // hardened for separators above, closed the same way.
        var offending = operationKey.Where(c => invalid.Contains(c) || char.IsControl(c)).Distinct().ToArray();
        if (offending.Length > 0)
        {
            reason = $"operationId '{operationKey}' cannot be a fixture filename: it contains " +
                     $"{string.Join(", ", offending.Select(c => $"'{c}'"))}. Change the operationId " +
                     "in the OpenAPI document — it also names generated client methods, so a " +
                     "filename-safe value is worth having anyway.";
            return false;
        }

        if (ReservedNames.Contains(operationKey, StringComparer.OrdinalIgnoreCase))
        {
            reason = $"operationId '{operationKey}' is a reserved device name on Windows and cannot " +
                     "be a filename. Change the operationId in the OpenAPI document.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Only valid for a key that has already passed <see cref="TryValidateOperationKey"/>.
    /// Throws otherwise, because reaching here with an unusable key means a caller skipped
    /// validation — an invariant violation rather than a condition to handle.
    /// </summary>
    public static string FileNameFor(string operationKey)
    {
        if (!TryValidateOperationKey(operationKey, out var reason))
        {
            throw new FixtureFormatException(reason);
        }

        return operationKey + ".json";
    }

    /// <summary>
    /// Serializes '$meta' first, then '$parameters' (sorted, since <see cref="Parameters"/> is a
    /// <see cref="SortedDictionary{TKey,TValue}"/>), then 'body' — 'body' omitted entirely, not
    /// emitted as 'null', when the operation takes none. That fixed order and omission is what
    /// keeps a committed fixture's diff reviewable: two documents with the same content always
    /// serialize byte-for-byte identically, and a change to one property changes one line.
    /// </summary>
    public string ToJson()
    {
        var root = new JsonObject
        {
            ["$meta"] = new JsonObject
            {
                ["tier"] = Meta.Tier,
                ["operationId"] = Meta.OperationId,
                ["generatedBy"] = Meta.GeneratedBy
            }
        };

        if (Parameters.Count > 0)
        {
            var parameters = new JsonObject();
            foreach (var (key, value) in Parameters)
            {
                parameters[key] = value;
            }
            root["$parameters"] = parameters;
        }

        if (Body is not null)
        {
            root["body"] = Body.DeepClone();
        }

        // NewLine = "\n" pins the interior line endings to LF (System.Text.Json otherwise uses
        // Environment.NewLine, CRLF on Windows); the trailing "+ \"\\n\"" is the final newline
        // after the closing brace, which WriteIndented never emits on its own. fixtures/ is
        // written only by `fixtures repair`, never generated wholesale like Generated/, so a
        // hand-edited value here is read closely — mixed line endings would bury the one changed
        // line in a whole-file diff, which is the failure this fix removes.
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" }) + "\n";
    }

    /// <summary>
    /// Parses a fixture. Fixtures are committed and hand-edited (see the class doc comment), so a
    /// malformed field — an unquoted number, a nested object where a string belongs — is a
    /// realistic typo, not adversarial input, and every failure surfaces as a
    /// <see cref="FixtureFormatException"/> naming the offending field with its inner exception
    /// preserved, the same idiom <c>SpecLoader</c> uses for external document parsing. Letting a
    /// framework exception like <c>InvalidOperationException</c> escape here would turn one
    /// malformed fixture into an unhandled crash for <c>FixtureStore</c> at runtime — the opposite
    /// of this file's "skip one bad thing, don't abandon the document" stance.
    /// <para>
    /// <c>$meta.tier</c> defaults to 4 (the worst tier) and <c>$meta.generatedBy</c> defaults to
    /// "unknown" when absent — both are provenance metadata a human can reasonably drop by hand.
    /// <c>$meta.operationId</c> has no such default: it is the key <c>fixtures repair</c> uses to
    /// tell "missing, needs fixing" from "present and correct", so an absent or blank value is
    /// malformed, the same as a missing '$meta' block.
    /// </para>
    /// </summary>
    public static FixtureDocument Parse(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException ex) { throw new FixtureFormatException($"Fixture is not valid JSON: {ex.Message}", ex); }

        if (root is not JsonObject obj)
        {
            throw new FixtureFormatException("Fixture root must be a JSON object.");
        }

        if (obj["$meta"] is not JsonObject meta)
        {
            throw new FixtureFormatException("Fixture is missing its '$meta' block. Regenerate it with `intest fixtures repair`.");
        }

        var document = new FixtureDocument
        {
            Meta = new FixtureMeta
            {
                Tier = ReadMetaInt(meta, "tier", 4),
                OperationId = ReadOperationId(meta),
                GeneratedBy = ReadMetaString(meta, "generatedBy", "unknown")
            },
            Body = obj["body"]?.DeepClone()
        };

        // An explicit JSON null reads as absent here, the same as 'body' above — that is how a
        // hand-editor writes "there are none". Any other non-object shape is malformed and must
        // say so: silently skipping it would load the fixture clean with every parameter missing,
        // and the request would go out malformed with nothing to point at.
        if (obj["$parameters"] is { } parametersNode)
        {
            if (parametersNode is not JsonObject parameters)
            {
                throw new FixtureFormatException(
                $"Fixture '$parameters' must be a JSON object of name/value pairs, but found " +
                $"'{parametersNode.ToJsonString()}'. Regenerate it with `intest fixtures repair`.");
            }

            foreach (var (key, value) in parameters)
            {
                try
                {
                    document.Parameters[key] = value?.GetValue<string>() ?? string.Empty;
                }
                catch (Exception ex) when (ex is InvalidOperationException or FormatException)
                {
                    throw new FixtureFormatException(
                        $"Fixture '$parameters.{key}' must be a string, but found '{value}'. " +
                        "Regenerate it with `intest fixtures repair`.", ex);
                }
            }
        }

        return document;
    }

    private static int ReadMetaInt(JsonObject meta, string field, int defaultValue)
    {
        var node = meta[field];
        if (node is null)
        {
            return defaultValue;
        }

        try
        {
            return node.GetValue<int>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new FixtureFormatException(
                $"Fixture '$meta.{field}' must be a number, but found '{node}'. " +
                "Regenerate it with `intest fixtures repair`.", ex);
        }
    }

    private static string ReadMetaString(JsonObject meta, string field, string defaultValue)
    {
        var node = meta[field];
        if (node is null)
        {
            return defaultValue;
        }

        try
        {
            return node.GetValue<string>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new FixtureFormatException(
                $"Fixture '$meta.{field}' must be a string, but found '{node}'. " +
                "Regenerate it with `intest fixtures repair`.", ex);
        }
    }

    private static string ReadOperationId(JsonObject meta)
    {
        var value = ReadMetaString(meta, "operationId", string.Empty);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FixtureFormatException(
            "Fixture is missing '$meta.operationId'. Regenerate it with `intest fixtures repair`.");
        }

        return value;
    }
}
