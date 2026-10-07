using System.Text.Json;

namespace HardwareTest.Authoring;

public static partial class AuthoringBuildService
{
    // MSBuild item, property and metadata identifiers are case insensitive, including
    // metadata spelling preserved in read-only SDK JSON output. XML structural tags are not.
    private static bool ShellIdentifierIs(string name, params string[] candidates)
        => candidates.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static bool TryShellJsonProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
            if (ShellIdentifierIs(property.Name, name))
            {
                value = property.Value;
                return true;
            }
        value = default;
        return false;
    }

    private static JsonElement ShellJsonProperty(JsonElement element, string name)
        => TryShellJsonProperty(element, name, out var value) ? value
            : throw new JsonException($"SDK evaluation omitted '{name}'.");
}
