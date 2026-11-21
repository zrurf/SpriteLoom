namespace SpriteLoom.Abstractions;

public interface IPacker
{
    string Id { get; }

    IReadOnlyList<PageLayout> Pack(IReadOnlyList<PackItem> items, PackOptions options);
}
