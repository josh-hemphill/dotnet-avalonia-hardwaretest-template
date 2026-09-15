using System.Text.Json;
using System.Text.Json.Serialization;

namespace HardwareTest;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(ShellApplicationPackageManifest))]
internal sealed partial class ShellApplicationJsonContext : JsonSerializerContext;
