using System.Text;
using System.Text.Json;

namespace WindowsTools.Modules.Agents.ClaudeCode;

/// <summary>
/// Incrementally reads one Claude Code transcript (<c>.jsonl</c>) and keeps only metadata:
/// token usage, model, session title and the tool currently in progress. Message text is never retained.
/// </summary>
internal sealed class ClaudeTranscriptReader
{
    private readonly HashSet<string> _countedResponses = new(StringComparer.Ordinal);
    private long _offset;

    public ClaudeTranscriptReader(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public TokenUsage Tokens { get; private set; }

    public string? Model { get; private set; }

    public string? CustomTitle { get; private set; }

    /// <summary>Gets the tool the model asked to run and whose result has not been written yet.</summary>
    public string? PendingTool { get; private set; }

    /// <summary>
    /// Reads lines appended since the previous call. A partially written last line is left for the next call.
    /// </summary>
    public void ReadNewLines()
    {
        using var stream = new FileStream(
            Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024);

        if (stream.Length < _offset)
        {
            // The file was rewritten (for example by a cleanup); start over.
            Reset();
        }

        if (stream.Length == _offset)
        {
            return;
        }

        stream.Seek(_offset, SeekOrigin.Begin);
        var buffer = new byte[stream.Length - _offset];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

        var lastNewline = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
        if (lastNewline < 0)
        {
            return;
        }

        var start = 0;
        for (var i = 0; i <= lastNewline; i++)
        {
            if (buffer[i] != (byte)'\n')
            {
                continue;
            }

            if (i > start)
            {
                ProcessLine(buffer.AsSpan(start, i - start));
            }

            start = i + 1;
        }

        _offset += lastNewline + 1;
    }

    internal void ProcessLine(ReadOnlySpan<byte> utf8Line)
    {
        JsonDocument doc;
        try
        {
            var reader = new Utf8JsonReader(utf8Line);
            doc = JsonDocument.ParseValue(ref reader);
        }
        catch (JsonException)
        {
            // Unknown or corrupted line: skip it, never fail the whole transcript.
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            switch (typeElement.GetString())
            {
                case "assistant":
                    ProcessAssistant(root);
                    break;
                case "user":
                    // A user record after a tool request carries the tool result, so the tool has finished.
                    PendingTool = null;
                    break;
                case "custom-title" when root.TryGetProperty("customTitle", out var title) && title.ValueKind == JsonValueKind.String:
                    CustomTitle = title.GetString();
                    break;
                default:
                    break;
            }
        }
    }

    private void ProcessAssistant(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var model = GetString(message, "model");
        if (model is null || model.StartsWith('<'))
        {
            // Synthetic records (for example "<synthetic>") are not model calls.
            return;
        }

        Model = model;

        // One response is written as several lines (one per content block) that repeat the same usage.
        // Count each response once, keyed by message id and request id.
        var key = string.Concat(GetString(message, "id"), ":", GetString(root, "requestId"));
        if (message.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object && _countedResponses.Add(key))
        {
            Tokens += new TokenUsage(
                GetLong(usage, "input_tokens"),
                GetLong(usage, "output_tokens"),
                GetLong(usage, "cache_creation_input_tokens"),
                GetLong(usage, "cache_read_input_tokens"));
        }

        if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind == JsonValueKind.Object && GetString(block, "type") == "tool_use")
                {
                    PendingTool = GetString(block, "name");
                }
            }
        }
    }

    private void Reset()
    {
        _offset = 0;
        _countedResponses.Clear();
        Tokens = default;
        Model = null;
        CustomTitle = null;
        PendingTool = null;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long GetLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : 0;

    /// <summary>
    /// Claude Code stores transcripts under a folder derived from the working directory with every
    /// non-alphanumeric character replaced by <c>-</c>.
    /// </summary>
    internal static string ProjectFolderName(string cwd)
    {
        var builder = new StringBuilder(cwd.Length);
        foreach (var ch in cwd)
        {
            builder.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '-');
        }

        return builder.ToString();
    }
}
