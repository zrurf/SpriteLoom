using SpriteLoom.Abstractions;

namespace SpriteLoom.Pipeline;

public sealed record PageLayoutSet(IReadOnlyList<PageLayout> Pages);
