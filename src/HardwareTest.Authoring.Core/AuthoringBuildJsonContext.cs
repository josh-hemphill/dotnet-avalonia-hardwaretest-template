using System.Text.Json.Serialization;

namespace HardwareTest.Authoring;

[JsonSerializable(typeof(AuthoringBuildReceipt))]
[JsonSerializable(typeof(AuthoringBuildResult))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
internal partial class AuthoringBuildJsonContext : JsonSerializerContext;
