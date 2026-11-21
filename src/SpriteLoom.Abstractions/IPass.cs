namespace SpriteLoom.Abstractions;

public interface IPass
{
    PassDescriptor Descriptor { get; }

    ValueTask<IReadOnlyDictionary<string, Artifact>> ExecuteAsync(
        PassContext context,
        IReadOnlyDictionary<string, Artifact> inputs);
}
