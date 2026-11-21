using System.Buffers;
using System.Text;
using SpriteLoom.Foundation;

namespace SpriteLoom.Model.Iri;

public sealed class SloomIri : IEquatable<SloomIri>
{
    private const int MaxUtf8Length = 512;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public SloomIriKind Kind { get; }

    public string PackageId { get; }

    public string Path { get; }

    public string Value { get; }

    private SloomIri(SloomIriKind kind, string packageId, string path, string value)
    {
        Kind = kind;
        PackageId = packageId;
        Path = path;
        Value = value;
    }

    public static SloomIri Parse(string value)
    {
        if (!TryParse(value, out var iri, out var diagnostic))
        {
            throw new SloomIriException(diagnostic!.Code, diagnostic.Message);
        }

        return iri!;
    }

    public static bool TryParse(string? value, out SloomIri? iri, out Diagnostic? diagnostic)
    {
        iri = null;

        if (!TryNormalize(value, out var canonical, out var kind, out var packageId, out var path, out diagnostic))
        {
            return false;
        }

        iri = new SloomIri(kind, packageId!, path!, canonical!);
        return true;
    }

    public static string Normalize(string value)
    {
        if (!TryNormalize(value, out var canonical, out _, out _, out _, out var diagnostic))
        {
            throw new SloomIriException(diagnostic!.Code, diagnostic.Message);
        }

        return canonical!;
    }

    public static bool TryNormalize(string? value, out string? canonical, out Diagnostic? diagnostic) =>
        TryNormalize(value, out canonical, out _, out _, out _, out diagnostic);

    public static SloomIri Create(SloomIriKind kind, string packageId, string path)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(path);

        var scheme = kind switch
        {
            SloomIriKind.Package => "pkg",
            SloomIriKind.Frame => "frame",
            SloomIriKind.Animation => "anim",
            SloomIriKind.Page => "page",
            SloomIriKind.Blob => "blob",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        var value = path.Length == 0
            ? $"{scheme}://{packageId}"
            : $"{scheme}://{packageId}/{path}";

        return Parse(value);
    }

    private static bool TryNormalize(
        string? value,
        out string? canonical,
        out SloomIriKind kind,
        out string? packageId,
        out string? path,
        out Diagnostic? diagnostic)
    {
        canonical = null;
        kind = default;
        packageId = null;
        path = null;
        diagnostic = null;

        if (string.IsNullOrEmpty(value))
        {
            diagnostic = Diagnostic.Error(IriDiagnostics.InvalidSyntax, "IRI must not be empty.");
            return false;
        }

        if (!HasValidUtf16(value))
        {
            diagnostic = Diagnostic.Error(IriDiagnostics.InvalidSyntax, "IRI contains an unpaired surrogate.");
            return false;
        }

        value = value.Normalize(NormalizationForm.FormC);

        var schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd <= 0)
        {
            diagnostic = Diagnostic.Error(IriDiagnostics.InvalidSyntax, "IRI must contain '://'.");
            return false;
        }

        var schemeText = value[..schemeEnd];
        if (!TryMapScheme(schemeText, out kind, out var scheme))
        {
            diagnostic = Diagnostic.Error(IriDiagnostics.UnknownScheme, $"Unknown scheme '{schemeText}'.");
            return false;
        }

        var authorityStart = schemeEnd + 3;
        var pathStart = value.IndexOf('/', authorityStart);
        var authorityText = pathStart < 0
            ? value[authorityStart..]
            : value[authorityStart..pathStart];
        var rawPath = pathStart < 0 ? string.Empty : value[pathStart..];

        if (!TryNormalizePackageId(authorityText, out packageId, out diagnostic))
        {
            return false;
        }

        if (kind == SloomIriKind.Package)
        {
            if (rawPath.Length != 0)
            {
                diagnostic = Diagnostic.Error(IriDiagnostics.InvalidPath, "The pkg scheme does not accept a path.");
                return false;
            }

            canonical = $"pkg://{packageId}";
            path = string.Empty;
            return CheckLength(canonical, out diagnostic);
        }

        if (!TryNormalizePath(rawPath, out path, out diagnostic))
        {
            return false;
        }

        canonical = $"{scheme}://{packageId}/{path}";
        return CheckLength(canonical, out diagnostic);
    }

    private static bool TryMapScheme(string text, out SloomIriKind kind, out string scheme)
    {
        if (text.Equals("pkg", StringComparison.OrdinalIgnoreCase))
        {
            kind = SloomIriKind.Package;
            scheme = "pkg";
            return true;
        }

        if (text.Equals("frame", StringComparison.OrdinalIgnoreCase))
        {
            kind = SloomIriKind.Frame;
            scheme = "frame";
            return true;
        }

        if (text.Equals("anim", StringComparison.OrdinalIgnoreCase))
        {
            kind = SloomIriKind.Animation;
            scheme = "anim";
            return true;
        }

        if (text.Equals("page", StringComparison.OrdinalIgnoreCase))
        {
            kind = SloomIriKind.Page;
            scheme = "page";
            return true;
        }

        if (text.Equals("blob", StringComparison.OrdinalIgnoreCase))
        {
            kind = SloomIriKind.Blob;
            scheme = "blob";
            return true;
        }

        kind = default;
        scheme = string.Empty;
        return false;
    }

    private static bool TryNormalizePackageId(string text, out string? packageId, out Diagnostic? diagnostic)
    {
        packageId = null;
        diagnostic = null;

        if (text.Length is < 1 or > 63)
        {
            diagnostic = Diagnostic.Error(IriDiagnostics.InvalidSyntax, "Package id must be 1 to 63 characters.");
            return false;
        }

        var normalized = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is >= 'A' and <= 'Z')
            {
                normalized.Append((char)(c + 32));
            }
            else
            {
                normalized.Append(c);
            }
        }

        var id = normalized.ToString();
        if (!IsLowerAlphaNumeric(id[0]) || !IsLowerAlphaNumeric(id[^1]))
        {
            diagnostic = Diagnostic.Error(IriDiagnostics.InvalidSyntax, "Package id must start and end with a letter or digit.");
            return false;
        }

        foreach (var c in id)
        {
            if (!IsLowerAlphaNumeric(c) && c is not '-' and not '.' and not '_')
            {
                diagnostic = Diagnostic.Error(IriDiagnostics.InvalidSyntax, $"Invalid package id character '{c}'.");
                return false;
            }
        }

        packageId = id;
        return true;
    }

    private static bool TryNormalizePath(string rawPath, out string? path, out Diagnostic? diagnostic)
    {
        path = null;
        diagnostic = null;

        if (rawPath.Length < 2 || rawPath[0] != '/')
        {
            diagnostic = Diagnostic.Error(IriDiagnostics.InvalidPath, "Path must contain at least one segment.");
            return false;
        }

        var segments = rawPath[1..].Split('/');
        var normalized = new StringBuilder(rawPath.Length);

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment.Length == 0)
            {
                diagnostic = Diagnostic.Error(IriDiagnostics.InvalidPath, "Path contains an empty segment.");
                return false;
            }

            if (segment is "." or "..")
            {
                diagnostic = Diagnostic.Error(IriDiagnostics.InvalidPath, "Path segments '.' and '..' are not allowed.");
                return false;
            }

            if (index > 0)
            {
                normalized.Append('/');
            }

            if (!TryNormalizeSegment(segment, normalized, out diagnostic))
            {
                return false;
            }
        }

        path = normalized.ToString();
        return true;
    }

    private static bool TryNormalizeSegment(string segment, StringBuilder output, out Diagnostic? diagnostic)
    {
        diagnostic = null;
        var index = 0;

        while (index < segment.Length)
        {
            if (segment[index] == '%')
            {
                if (!TryDecodePercentRun(segment, ref index, out var decoded, out diagnostic))
                {
                    return false;
                }

                foreach (var runeInDecoded in decoded.EnumerateRunes())
                {
                    if (!TryAppendRune(runeInDecoded, output, out diagnostic))
                    {
                        return false;
                    }
                }

                continue;
            }

            if (!TryReadRune(segment, ref index, out var rune, out diagnostic))
            {
                return false;
            }

            if (!TryAppendRune(rune, output, out diagnostic))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryDecodePercentRun(
        string segment,
        ref int index,
        out string decoded,
        out Diagnostic? diagnostic)
    {
        decoded = string.Empty;
        diagnostic = null;
        var bytes = new List<byte>();

        while (index < segment.Length && segment[index] == '%')
        {
            if (index + 2 >= segment.Length)
            {
                diagnostic = Diagnostic.Error(IriDiagnostics.InvalidSyntax, "Truncated percent-encoding.");
                return false;
            }

            var high = HexValue(segment[index + 1]);
            var low = HexValue(segment[index + 2]);
            if (high < 0 || low < 0)
            {
                diagnostic = Diagnostic.Error(IriDiagnostics.InvalidSyntax, "Percent-encoding requires two hexadecimal digits.");
                return false;
            }

            bytes.Add((byte)((high << 4) | low));
            index += 3;
        }

        try
        {
            decoded = StrictUtf8.GetString(bytes.ToArray());
        }
        catch (DecoderFallbackException)
        {
            diagnostic = Diagnostic.Error(IriDiagnostics.InvalidSyntax, "Percent-encoded bytes are not valid UTF-8.");
            return false;
        }

        return true;
    }

    private static bool TryReadRune(
        string text,
        ref int index,
        out Rune rune,
        out Diagnostic? diagnostic)
    {
        diagnostic = null;
        var status = Rune.DecodeFromUtf16(text.AsSpan(index), out rune, out var consumed);
        if (status != OperationStatus.Done)
        {
            diagnostic = Diagnostic.Error(IriDiagnostics.InvalidSyntax, "IRI contains invalid UTF-16.");
            return false;
        }

        index += consumed;
        return true;
    }

    private static bool TryAppendRune(Rune rune, StringBuilder output, out Diagnostic? diagnostic)
    {
        diagnostic = null;

        if (Rune.IsWhiteSpace(rune) || rune.Value < 0x20 || rune.Value == 0x7F)
        {
            diagnostic = Diagnostic.Error(IriDiagnostics.ForbiddenCharacter, "IRI must not contain whitespace or control characters.");
            return false;
        }

        if (rune.Value is '?' or '#')
        {
            diagnostic = Diagnostic.Error(IriDiagnostics.ForbiddenCharacter, "IRI must not contain '?' or '#'.");
            return false;
        }

        if (IsPrivateUse(rune.Value))
        {
            diagnostic = Diagnostic.Error(IriDiagnostics.ForbiddenCharacter, "Private-use characters are not allowed.");
            return false;
        }

        if (IsIUnreserved(rune))
        {
            output.Append(rune.ToString());
            return true;
        }

        Span<byte> buffer = stackalloc byte[4];
        var length = rune.EncodeToUtf8(buffer);
        for (var i = 0; i < length; i++)
        {
            output.Append('%');
            output.Append(ToHex(buffer[i] >> 4));
            output.Append(ToHex(buffer[i] & 0x0F));
        }

        return true;
    }

    private static bool CheckLength(string canonical, out Diagnostic? diagnostic)
    {
        if (Encoding.UTF8.GetByteCount(canonical) > MaxUtf8Length)
        {
            diagnostic = Diagnostic.Error(IriDiagnostics.InvalidSyntax, "IRI exceeds 512 UTF-8 bytes.");
            return false;
        }

        diagnostic = null;
        return true;
    }

    private static bool HasValidUtf16(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    return false;
                }

                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLowerAlphaNumeric(char c) =>
        c is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static bool IsPrivateUse(int value) => value is >= 0xE000 and <= 0xF8FF;

    private static bool IsIUnreserved(Rune rune)
    {
        var value = rune.Value;
        if (value < 0x80)
        {
            return value is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '-' or '.' or '_' or '~';
        }

        return value is >= 0x00A0 and <= 0xD7FF
            or >= 0xF900 and <= 0xFDCF
            or >= 0xFDF0 and <= 0xFFEF
            or >= 0x10000 and <= 0xEFFFD;
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1
    };

    private static char ToHex(int value) => (char)(value < 10 ? '0' + value : 'A' + value - 10);

    public bool Equals(SloomIri? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is SloomIri other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(SloomIri? left, SloomIri? right) => Equals(left, right);

    public static bool operator !=(SloomIri? left, SloomIri? right) => !Equals(left, right);
}
