namespace SpriteLoom.Foundation;

public sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var xIndex = 0;
        var yIndex = 0;

        while (xIndex < x.Length && yIndex < y.Length)
        {
            var xDigit = IsDigit(x[xIndex]);
            var yDigit = IsDigit(y[yIndex]);

            if (xDigit && yDigit)
            {
                var comparison = CompareNumberRun(x, ref xIndex, y, ref yIndex);
                if (comparison != 0)
                {
                    return comparison;
                }

                continue;
            }

            if (x[xIndex] != y[yIndex])
            {
                return x[xIndex].CompareTo(y[yIndex]);
            }

            xIndex++;
            yIndex++;
        }

        var lengthComparison = (x.Length - xIndex).CompareTo(y.Length - yIndex);
        return lengthComparison != 0 ? lengthComparison : string.CompareOrdinal(x, y);
    }

    private static int CompareNumberRun(string x, ref int xIndex, string y, ref int yIndex)
    {
        var xStart = xIndex;
        var yStart = yIndex;

        while (xIndex < x.Length && IsDigit(x[xIndex]))
        {
            xIndex++;
        }

        while (yIndex < y.Length && IsDigit(y[yIndex]))
        {
            yIndex++;
        }

        var xDigits = x.AsSpan(xStart, xIndex - xStart);
        var yDigits = y.AsSpan(yStart, yIndex - yStart);

        var xSignificant = TrimLeadingZeros(xDigits);
        var ySignificant = TrimLeadingZeros(yDigits);

        if (xSignificant.Length != ySignificant.Length)
        {
            return xSignificant.Length.CompareTo(ySignificant.Length);
        }

        var comparison = xSignificant.SequenceCompareTo(ySignificant);
        if (comparison != 0)
        {
            return comparison;
        }

        return yDigits.Length.CompareTo(xDigits.Length);
    }

    private static ReadOnlySpan<char> TrimLeadingZeros(ReadOnlySpan<char> digits)
    {
        var index = 0;
        while (index < digits.Length - 1 && digits[index] == '0')
        {
            index++;
        }

        return digits[index..];
    }

    private static bool IsDigit(char value) => value is >= '0' and <= '9';
}
