using System.Text.Json;
using System.Text.Json.Serialization;

namespace HardwareTest.Authoring;

public static partial class AuthoringBuildService
{
    private static void ValidateSourceShape(byte[] bytes, bool workspace)
    {
        var options = new JsonSerializerOptions(AuthoringDocumentJsonContext.Default.Options)
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        var context = new AuthoringDocumentJsonContext(options);
        try
        {
            if (workspace) _ = JsonSerializer.Deserialize(bytes, context.AuthoringWorkspaceDto);
            else _ = JsonSerializer.Deserialize(bytes, context.AuthoringDocumentDto);
        }
        catch (JsonException error)
        {
            throw new AuthoringWorkspaceException("BUILD_SOURCE: Unknown or invalid saved source content. Explicit repair is required.", error);
        }
    }
}
