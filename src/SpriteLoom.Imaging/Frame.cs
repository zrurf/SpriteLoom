using SpriteLoom.Foundation;
using SpriteLoom.Model.Iri;

namespace SpriteLoom.Imaging;

public sealed class Frame
{
    public required SloomIri Uri { get; init; }

    public required string SourceId { get; init; }

    public required int SourceIndex { get; init; }

    public required RgbaImage Pixels { get; init; }

    public required int OriginWidth { get; init; }

    public required int OriginHeight { get; init; }

    public required int OffsetX { get; init; }

    public required int OffsetY { get; init; }

    public required uint PivotX { get; init; }

    public required uint PivotY { get; init; }

    public required ContentHash ContentHash { get; init; }

    public bool Trimmed { get; init; }

    public int StoredIndex { get; set; } = -1;
}
