namespace SpriteLoom.Model.Iri;

public sealed class SloomIriException : Exception
{
    public string Code { get; }

    public SloomIriException(string code, string message)
        : base($"{code}: {message}")
    {
        Code = code;
    }
}
