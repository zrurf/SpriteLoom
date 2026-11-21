using SpriteLoom.Abstractions;
using SpriteLoom.Imaging;

namespace SpriteLoom.Pipeline.Passes;

public sealed class PackPagesPass : IPass
{
    private readonly IPacker _packer;

    public PackPagesPass(IPacker packer)
    {
        _packer = packer;
    }

    public PassDescriptor Descriptor { get; } = new(
        "pack.pages",
        "1.0",
        [new PortSpec("in", ArtifactKind.FrameSet)],
        [new PortSpec("out", ArtifactKind.PageSet)],
        PassDeterminism.Deterministic);

    public ValueTask<IReadOnlyDictionary<string, Artifact>> ExecuteAsync(
        PassContext context,
        IReadOnlyDictionary<string, Artifact> inputs)
    {
        var frames = (FrameSet)inputs["in"].Payload;
        var items = frames.Stored
            .Select((frame, index) => new PackItem(index, frame.Pixels.Width, frame.Pixels.Height))
            .ToArray();

        var options = new PackOptions(
            PassParameters.GetInt(context.Parameters, "maxWidth", 2048),
            PassParameters.GetInt(context.Parameters, "maxHeight", 2048),
            PassParameters.GetInt(context.Parameters, "padding", 2),
            PassParameters.GetBool(context.Parameters, "pow2", false),
            PassParameters.GetInt(context.Parameters, "align", 4),
            PassParameters.GetBool(context.Parameters, "allowRotation", false));

        var layouts = _packer.Pack(items, options);

        return ValueTask.FromResult<IReadOnlyDictionary<string, Artifact>>(
            new Dictionary<string, Artifact>(StringComparer.Ordinal)
            {
                ["out"] = new Artifact(ArtifactKind.PageSet, 0, new PageLayoutSet(layouts))
            });
    }
}
