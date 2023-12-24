using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Credfeto.DotNet.Repo.Tools.Cmd.Packages;

[SuppressMessage(category: "ReSharper", checkId: "PartialTypeWithSinglePart", Justification = "Required for JsonSerializerContext")]
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization | JsonSourceGenerationMode.Metadata,
                             PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
                             DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                             WriteIndented = false,
                             IncludeFields = false)]
[JsonSerializable(typeof(PackageUpdate))]
[JsonSerializable(typeof(IReadOnlyList<PackageUpdate>))]
[JsonSerializable(typeof(PackageExclude))]
[JsonSerializable(typeof(IReadOnlyList<PackageExclude>))]
internal sealed partial class PackageConfigSerializationContext : JsonSerializerContext
{
}