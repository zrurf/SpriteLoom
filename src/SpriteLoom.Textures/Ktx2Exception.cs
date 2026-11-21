namespace SpriteLoom.Textures;

public sealed class Ktx2Exception : Exception
{
    public Ktx2Exception(string code, string message)
        : base($"{code}: {message}")
    {
        Code = code;
    }

    public string Code { get; }
}
