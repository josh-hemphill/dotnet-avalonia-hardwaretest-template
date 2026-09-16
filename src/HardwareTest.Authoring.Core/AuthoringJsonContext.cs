using System.Text.Json.Serialization;

namespace HardwareTest.Authoring;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AuthoringManifest))]
[JsonSerializable(typeof(AuthoringPackageSpec))]
[JsonSerializable(typeof(AuthoringPackageDependency))]
[JsonSerializable(typeof(AuthoringOptionalDependency))]
[JsonSerializable(typeof(List<AuthoringPackageDependency>))]
[JsonSerializable(typeof(List<AuthoringOptionalDependency>))]
[JsonSerializable(typeof(List<string>))]
public partial class AuthoringJsonContext : JsonSerializerContext;
