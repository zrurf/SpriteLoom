namespace SpriteLoom.Bundle;

public static class SlbKinds
{
    public static SlbBlockKind Infer(string path)
    {
        if (path is "manifest.json" or "manifest.cbor")
        {
            return SlbBlockKind.Manifest;
        }

        if (path.StartsWith("page/", StringComparison.Ordinal) &&
            path.Contains(".slatlas/container.", StringComparison.Ordinal))
        {
            return SlbBlockKind.Container;
        }

        if (path.StartsWith("page/", StringComparison.Ordinal) &&
            path.Contains(".slatlas/", StringComparison.Ordinal))
        {
            return SlbBlockKind.Image;
        }

        if (path.StartsWith("anim/", StringComparison.Ordinal))
        {
            return SlbBlockKind.Animation;
        }

        if (path.StartsWith("signatures/", StringComparison.Ordinal))
        {
            return SlbBlockKind.Signature;
        }

        return SlbBlockKind.Blob;
    }

    public static string GetName(SlbBlockKind kind) => kind switch
    {
        SlbBlockKind.Manifest => "manifest",
        SlbBlockKind.Container => "container",
        SlbBlockKind.Image => "image",
        SlbBlockKind.Animation => "animation",
        SlbBlockKind.Blob => "blob",
        SlbBlockKind.Mesh => "mesh",
        SlbBlockKind.Signature => "signature",
        SlbBlockKind.Checksum => "checksum",
        SlbBlockKind.Padding => "padding",
        SlbBlockKind.ExternalPart => "external-part",
        SlbBlockKind.Crypto => "crypto",
        _ => "unknown"
    };

    public static SlbBlockKind Parse(string name) => name switch
    {
        "manifest" => SlbBlockKind.Manifest,
        "container" => SlbBlockKind.Container,
        "image" => SlbBlockKind.Image,
        "animation" => SlbBlockKind.Animation,
        "blob" => SlbBlockKind.Blob,
        "mesh" => SlbBlockKind.Mesh,
        "signature" => SlbBlockKind.Signature,
        "checksum" => SlbBlockKind.Checksum,
        "padding" => SlbBlockKind.Padding,
        "external-part" => SlbBlockKind.ExternalPart,
        "crypto" => SlbBlockKind.Crypto,
        _ => throw BundleDiagnostics.Error(
            BundleDiagnostics.UnknownBlockKind,
            $"Unknown block kind: {name}")
    };
}
