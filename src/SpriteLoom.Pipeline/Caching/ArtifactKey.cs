using System.IO.Hashing;
using System.Text;
using SpriteLoom.Abstractions;

namespace SpriteLoom.Pipeline.Caching;

internal static class ArtifactKey
{
    public static string Compute(PassNode node, IReadOnlyDictionary<string, Artifact> inputs)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, new UTF8Encoding(false), leaveOpen: true);

        writer.Write(node.Pass.Descriptor.Id);
        writer.Write(node.Pass.Descriptor.Version);
        WriteParameters(writer, node.Parameters);
        writer.Write(inputs.Count);

        foreach (var (port, artifact) in inputs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.Write(port);
            ArtifactCodec.Write(writer, artifact);
        }

        writer.Flush();
        var hash = XxHash128.Hash(stream.GetBuffer().AsSpan(0, (int)stream.Length));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void WriteParameters(BinaryWriter writer, IReadOnlyDictionary<string, object?> parameters)
    {
        writer.Write(parameters.Count);

        foreach (var (key, value) in parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.Write(key);

            switch (value)
            {
                case null:
                    writer.Write((byte)0);
                    break;
                case string text:
                    writer.Write((byte)1);
                    writer.Write(text);
                    break;
                case int number:
                    writer.Write((byte)2);
                    writer.Write(number);
                    break;
                case bool flag:
                    writer.Write((byte)3);
                    writer.Write(flag);
                    break;
                case IReadOnlyList<SourceSpec> sources:
                    writer.Write((byte)4);
                    writer.Write(sources.Count);

                    foreach (var source in sources)
                    {
                        writer.Write(source.Id);
                        writer.Write(source.Kind);
                        writer.Write(source.Path);
                        writer.Write(source.ImagePath is not null);
                        if (source.ImagePath is not null)
                        {
                            writer.Write(source.ImagePath);
                        }

                        writer.Write(source.Order);
                        writer.Write(source.FrameDurationMs);
                    }

                    break;
                case IReadOnlyList<AnimationSpec> animations:
                    writer.Write((byte)5);
                    writer.Write(animations.Count);

                    foreach (var animation in animations)
                    {
                        writer.Write(animation.Uri);
                        writer.Write(animation.Source);
                        writer.Write(animation.Loop);
                        writer.Write(animation.Playback);
                        writer.Write(animation.DurationMs);
                    }

                    break;
                case IReadOnlyList<string> values:
                    writer.Write((byte)6);
                    writer.Write(values.Count);

                    foreach (var item in values)
                    {
                        writer.Write(item);
                    }

                    break;
                default:
                    throw new InvalidOperationException(
                        $"Parameter '{key}' of type '{value.GetType().Name}' cannot be cached.");
            }
        }
    }
}
