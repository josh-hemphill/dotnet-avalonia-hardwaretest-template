using System.Text.Json.Serialization;

namespace HardwareTest.Authoring;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AuthoringManifest))]
[JsonSerializable(typeof(AuthoringWorkspaceCatalogs))]
[JsonSerializable(typeof(AuthoringPackageSpec))]
[JsonSerializable(typeof(AuthoringPackageDependency))]
[JsonSerializable(typeof(AuthoringOptionalDependency))]
[JsonSerializable(typeof(List<AuthoringPackageDependency>))]
[JsonSerializable(typeof(List<AuthoringOptionalDependency>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(ShipManifest))]
[JsonSerializable(typeof(ShipDependency))]
[JsonSerializable(typeof(List<ShipDependency>))]
[JsonSerializable(typeof(TfModelJson))]
[JsonSerializable(typeof(AuthoringPreferences))]
[JsonSerializable(typeof(double[]))]
public partial class AuthoringJsonContext : JsonSerializerContext;
