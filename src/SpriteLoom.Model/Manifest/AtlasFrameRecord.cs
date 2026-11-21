namespace SpriteLoom.Model.Manifest;

public sealed record AtlasFrameRecord(
    int Page,
    int Stored,
    int OriginWidth,
    int OriginHeight,
    int OffsetX,
    int OffsetY,
    uint PivotX,
    uint PivotY,
    uint Flags);
