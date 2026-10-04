using System.Text.Json.Serialization;

namespace HardwareTest.Authoring;

[JsonSerializable(typeof(AuthoringBuildReceipt))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
internal partial class AuthoringBuildJsonContext : JsonSerializerContext;
