using System.Text.Json;

namespace HardwareTest.Authoring;

public interface IAuthoringPreferencesStore
{
    AuthoringPreferences Current { get; }

    bool IsReadOnly { get; }

    string? Warning { get; }

    string FilePath { get; }

    void Load();

    void Save();
}

/// Load/save versioned authoring-preferences.json. Missing file is defaults; unknown props fail closed.
public sealed class AuthoringPreferencesStore : IAuthoringPreferencesStore
{
    public const string FileName = "authoring-preferences.json";

    public const string ProductDirectoryName = "HardwareTest";

    private static readonly HashSet<string> KnownProperties = new(StringComparer.Ordinal)
    {
        "schemaVersion",
        "themePreference",
        "lastWorkspace",
        "openTapHomeOverride",
        "showRawStepXml",
    };

    public AuthoringPreferencesStore(string? filePath = null)
    {
        FilePath = string.IsNullOrWhiteSpace(filePath)
            ? DefaultFilePath()
            : Path.GetFullPath(filePath);
        Current = new AuthoringPreferences();
    }

    public AuthoringPreferences Current { get; private set; }

    public bool IsReadOnly { get; private set; }

    public string? Warning { get; private set; }

    public string FilePath { get; }

    public static string DefaultFilePath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ProductDirectoryName,
            FileName);

    public void Load()
    {
        Warning = null;
        IsReadOnly = false;
        Current = new AuthoringPreferences();
        if (!File.Exists(FilePath))
        {
            return;
        }

        string raw;
        try
        {
            raw = File.ReadAllText(FilePath);
        }
        catch (Exception ex)
        {
            throw new AuthoringPreferencesException($"Failed to read {FilePath}.", ex);
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new AuthoringPreferencesException($"Empty {FileName} at {FilePath}.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw);
        }
        catch (Exception ex)
        {
            throw new AuthoringPreferencesException($"Invalid JSON in {FilePath}: {ex.Message}", ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new AuthoringPreferencesException($"{FileName} must be a JSON object.");
            }

            var schemaVersion = ReadSchemaVersion(document.RootElement);
            var isFuture = schemaVersion > AuthoringSchemaVersions.Preferences;
            if (!isFuture)
            {
                RejectUnknownProperties(document.RootElement);
            }

            AuthoringPreferences preferences;
            try
            {
                preferences = JsonSerializer.Deserialize(raw, AuthoringJsonContext.Default.AuthoringPreferences)
                    ?? throw new AuthoringPreferencesException($"Failed to deserialize {FilePath}.");
            }
            catch (AuthoringPreferencesException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AuthoringPreferencesException($"Invalid {FileName}: {ex.Message}", ex);
            }

            preferences.SchemaVersion = schemaVersion;
            preferences.ThemePreference = AuthoringThemePreference.Normalize(preferences.ThemePreference);
            Current = preferences;
            IsReadOnly = isFuture;
            Warning = isFuture
                ? $"{FileName} schema {schemaVersion} is newer than {AuthoringSchemaVersions.Preferences}; settings are read-only."
                : null;
        }
    }

    public void Save()
    {
        if (IsReadOnly)
        {
            throw new AuthoringPreferencesException(
                $"Refusing to overwrite future-schema {FileName} (schema {Current.SchemaVersion} > {AuthoringSchemaVersions.Preferences}).");
        }

        if (Current.SchemaVersion > AuthoringSchemaVersions.Preferences)
        {
            throw new AuthoringPreferencesException(
                $"Cannot write {FileName} schema {Current.SchemaVersion}; this app supports {AuthoringSchemaVersions.Preferences}.");
        }

        Current.SchemaVersion = AuthoringSchemaVersions.Preferences;
        Current.ThemePreference = AuthoringThemePreference.Normalize(Current.ThemePreference);
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(Current, AuthoringJsonContext.Default.AuthoringPreferences);
        File.WriteAllText(FilePath, json + Environment.NewLine);
    }

    private static int ReadSchemaVersion(JsonElement root)
    {
        if (!root.TryGetProperty("schemaVersion", out var versionElement)
            || versionElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new AuthoringPreferencesException($"{FileName} is missing required schemaVersion.");
        }

        if (versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out var version))
        {
            throw new AuthoringPreferencesException($"{FileName} schemaVersion must be a positive integer.");
        }

        if (version <= 0)
        {
            throw new AuthoringPreferencesException($"{FileName} schemaVersion must be a positive integer (got {version}).");
        }

        return version;
    }

    private void RejectUnknownProperties(JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!KnownProperties.Contains(property.Name))
            {
                throw new AuthoringPreferencesException($"Unknown property '{property.Name}' in {FilePath}.");
            }
        }
    }
}
