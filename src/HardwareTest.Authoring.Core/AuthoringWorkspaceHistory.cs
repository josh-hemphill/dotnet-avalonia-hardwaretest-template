using System.Text;
using System.Text.Json;

namespace HardwareTest.Authoring;

/// An isolated workspace value containing the manifest and every program draft.
public sealed class AuthoringWorkspaceState
{
    private readonly AuthoringManifest _manifest;
    private readonly AuthoringDocumentSnapshot[] _drafts;
    private readonly string _manifestIdentity;

    private AuthoringWorkspaceState(AuthoringManifest manifest, IReadOnlyList<ProgramDraft> drafts)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(drafts);
        _manifest = CloneManifest(manifest);
        _manifestIdentity = CanonicalManifest(_manifest);
        _drafts = drafts.Select(AuthoringDocumentSnapshot.Capture).ToArray();
    }

    public AuthoringManifest Manifest => CloneManifest(_manifest);
    public IReadOnlyList<ProgramDraft> Drafts => _drafts.Select(draft => draft.Restore()).ToArray();

    public static AuthoringWorkspaceState Capture(AuthoringManifest manifest, IReadOnlyList<ProgramDraft> drafts)
        => new(manifest, drafts);

    public bool ContentEquals(AuthoringWorkspaceState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return _manifestIdentity == other._manifestIdentity
            && _drafts.Length == other._drafts.Length
            && _drafts.Zip(other._drafts).All(pair => pair.First.ContentEquals(pair.Second));
    }

    private static AuthoringManifest CloneManifest(AuthoringManifest manifest)
        => JsonSerializer.Deserialize(
            JsonSerializer.Serialize(manifest, AuthoringJsonContext.Default.AuthoringManifest),
            AuthoringJsonContext.Default.AuthoringManifest)!;

    private static string CanonicalManifest(AuthoringManifest manifest)
    {
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(manifest, AuthoringJsonContext.Default.AuthoringManifest));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, document.RootElement);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}

/// Atomic manifest-and-program transactions guarded against changes outside this history.
public sealed class AuthoringWorkspaceHistory
{
    private sealed record Entry(string Description, AuthoringWorkspaceState Before, AuthoringWorkspaceState After);
    private readonly List<Entry> _entries = [];
    private int _position;

    public static AuthoringWorkspaceState Capture(AuthoringManifest manifest, IReadOnlyList<ProgramDraft> drafts)
        => AuthoringWorkspaceState.Capture(manifest, drafts);

    public bool Commit(string description, AuthoringWorkspaceState before, AuthoringWorkspaceState after)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.ContentEquals(after))
        {
            return false;
        }
        _entries.RemoveRange(_position, _entries.Count - _position);
        _entries.Add(new Entry(description, before, after));
        _position++;
        return true;
    }

    public bool CanUndo(AuthoringWorkspaceState current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return _position > 0 && current.ContentEquals(_entries[_position - 1].After);
    }

    public bool CanRedo(AuthoringWorkspaceState current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return _position < _entries.Count && current.ContentEquals(_entries[_position].Before);
    }

    public AuthoringWorkspaceState Undo(AuthoringWorkspaceState current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (_position == 0)
        {
            throw new InvalidOperationException("There is no workspace change to undo.");
        }
        var entry = _entries[_position - 1];
        RequireCurrent(current, entry.After);
        _position--;
        return entry.Before;
    }

    public AuthoringWorkspaceState Redo(AuthoringWorkspaceState current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (_position == _entries.Count)
        {
            throw new InvalidOperationException("There is no workspace change to redo.");
        }
        var entry = _entries[_position];
        RequireCurrent(current, entry.Before);
        _position++;
        return entry.After;
    }

    // Successful persistence may normalize content without creating an edit.
    public void RebaseCurrent(AuthoringWorkspaceState before, AuthoringWorkspaceState after)
    {
        if (_position > 0 && _entries[_position - 1].After.ContentEquals(before))
            _entries[_position - 1] = _entries[_position - 1] with { After = after };
        if (_position < _entries.Count && _entries[_position].Before.ContentEquals(before))
            _entries[_position] = _entries[_position] with { Before = after };
    }

    public void Clear()
    {
        _entries.Clear();
        _position = 0;
    }

    private static void RequireCurrent(AuthoringWorkspaceState current, AuthoringWorkspaceState expected)
    {
        if (!current.ContentEquals(expected))
        {
            throw new InvalidOperationException(
                "Workspace history is stale because the manifest or a program changed outside this transaction. "
                + "Undo intervening program edits before restoring this workspace operation.");
        }
    }
}
