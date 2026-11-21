namespace SpriteLoom.Foundation;

public sealed class PixelFormatException : Exception
{
    public PixelFormatException(string code, string message)
        : base($"{code}: {message}")
    {
        Code = code;
    }

    public string Code { get; }
}
