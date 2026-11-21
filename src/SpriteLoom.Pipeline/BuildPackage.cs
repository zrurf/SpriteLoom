using SpriteLoom.Model.Manifest;

namespace SpriteLoom.Pipeline;

public sealed record BuildPackage(
    AtlasManifest Manifest,
    IReadOnlyDictionary<string, byte[]> Files,
    ManifestFormat Format);

public sealed record PackageOutput(string Path);
