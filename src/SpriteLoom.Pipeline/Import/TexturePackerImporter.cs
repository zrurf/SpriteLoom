using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;
using SpriteLoom.Imaging;
using SpriteLoom.Model.Iri;

namespace SpriteLoom.Pipeline.Import;

internal static class TexturePackerImporter
{
    public static IReadOnlyList<Frame> Import(
        string metadataPath,
        string? imagePath,
        IImageCodec codec,
        string sourceId,
        string packageId)
    {
        var text = File.ReadAllText(metadataPath);
        var document = Path.GetExtension(metadataPath).ToLowerInvariant() switch
        {
            ".json" => ParseJson(text),
            ".plist" => ParsePlist(text),
            ".xml" => ParseXml(text),
            _ => throw new InvalidDataException(
                $"Unsupported TexturePacker metadata format: {metadataPath}")
        };

        var sheetPath = imagePath ?? document.Image;

        if (sheetPath is null)
        {
            throw new InvalidDataException("TexturePacker metadata does not declare a sheet image.");
        }

        if (!Path.IsPathRooted(sheetPath))
        {
            sheetPath = Path.Combine(Path.GetDirectoryName(metadataPath)!, sheetPath);
        }

        sheetPath = Path.GetFullPath(sheetPath);

        if (!File.Exists(sheetPath))
        {
            throw new FileNotFoundException("TexturePacker sheet image not found.", sheetPath);
        }

        var sheet = codec.Decode(File.ReadAllBytes(sheetPath));
        var ordered = document.Frames
            .OrderBy(frame => frame.Name, NaturalStringComparer.Instance)
            .ToArray();
        var frames = new List<Frame>(ordered.Length);

        for (var index = 0; index < ordered.Length; index++)
        {
            var item = ordered[index];

            if (item.Rect.IsEmpty ||
                item.Rect.Right > sheet.Width ||
                item.Rect.Bottom > sheet.Height)
            {
                throw new InvalidDataException($"Frame '{item.Name}' is outside the sheet image.");
            }

            var pixels = sheet.Crop(item.Rect);

            if (item.Rotated)
            {
                pixels = RotateCounterClockwise(pixels);
            }

            var originWidth = item.OriginWidth > 0 ? item.OriginWidth : pixels.Width;
            var originHeight = item.OriginHeight > 0 ? item.OriginHeight : pixels.Height;
            var pivotX = item.PivotX is { } pivotXValue
                ? (uint)Math.Round(pivotXValue * originWidth * 16)
                : (uint)(originWidth * 8);
            var pivotY = item.PivotY is { } pivotYValue
                ? (uint)Math.Round(pivotYValue * originHeight * 16)
                : (uint)(originHeight * 8);

            var uriPath = $"{sourceId}/{NormalizeName(item.Name)}";

            frames.Add(new Frame
            {
                Uri = SloomIri.Create(SloomIriKind.Frame, packageId, uriPath),
                SourceId = sourceId,
                SourceIndex = index,
                Pixels = pixels,
                OriginWidth = originWidth,
                OriginHeight = originHeight,
                OffsetX = item.OffsetX,
                OffsetY = item.OffsetY,
                PivotX = pivotX,
                PivotY = pivotY,
                ContentHash = ContentHash.Compute(pixels.Pixels),
                Trimmed = true
            });
        }

        return frames;
    }

    private static TexturePackerDocument ParseJson(string text)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var frames = new List<TexturePackerFrame>();
        var framesElement = root.GetProperty("frames");

        if (framesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in framesElement.EnumerateObject())
            {
                frames.Add(ParseJsonFrame(property.Name, property.Value));
            }
        }
        else if (framesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in framesElement.EnumerateArray())
            {
                var name = element.GetProperty("filename").GetString()
                    ?? throw new InvalidDataException("TexturePacker frame is missing a filename.");
                frames.Add(ParseJsonFrame(name, element));
            }
        }
        else
        {
            throw new InvalidDataException("TexturePacker 'frames' must be an object or an array.");
        }

        var image = root.TryGetProperty("meta", out var meta) &&
                    meta.TryGetProperty("image", out var imageElement)
            ? imageElement.GetString()
            : null;

        return new TexturePackerDocument(frames, image);
    }

    private static TexturePackerFrame ParseJsonFrame(string name, JsonElement element)
    {
        var frame = element.GetProperty("frame");
        var rect = new IntRect(
            frame.GetProperty("x").GetInt32(),
            frame.GetProperty("y").GetInt32(),
            frame.GetProperty("w").GetInt32(),
            frame.GetProperty("h").GetInt32());

        var rotated = element.TryGetProperty("rotated", out var rotatedElement) &&
                      rotatedElement.GetBoolean();
        var originWidth = rect.Width;
        var originHeight = rect.Height;

        if (element.TryGetProperty("sourceSize", out var sourceSize))
        {
            originWidth = sourceSize.GetProperty("w").GetInt32();
            originHeight = sourceSize.GetProperty("h").GetInt32();
        }

        var offsetX = 0;
        var offsetY = 0;
        var trimmed = element.TryGetProperty("trimmed", out var trimmedElement) &&
                      trimmedElement.GetBoolean();

        if (trimmed && element.TryGetProperty("spriteSourceSize", out var spriteSourceSize))
        {
            offsetX = spriteSourceSize.GetProperty("x").GetInt32();
            offsetY = spriteSourceSize.GetProperty("y").GetInt32();
        }

        double? pivotX = null;
        double? pivotY = null;

        if (element.TryGetProperty("pivot", out var pivot))
        {
            pivotX = pivot.GetProperty("x").GetDouble();
            pivotY = pivot.GetProperty("y").GetDouble();
        }

        return new TexturePackerFrame(
            name,
            rect,
            rotated,
            offsetX,
            offsetY,
            originWidth,
            originHeight,
            pivotX,
            pivotY);
    }

    private static TexturePackerDocument ParsePlist(string text)
    {
        var root = XDocument.Parse(text).Root
            ?? throw new InvalidDataException("Plist has no root element.");
        var plist = root.Elements().FirstOrDefault(element => element.Name.LocalName == "dict")
            ?? throw new InvalidDataException("Plist has no top-level dictionary.");
        var top = ReadDictionary(plist);

        var framesElement = top.GetValueOrDefault("frames")
            ?? throw new InvalidDataException("Plist has no frames dictionary.");
        var framesDictionary = ReadDictionary(framesElement);
        var frames = new List<TexturePackerFrame>(framesDictionary.Count);

        foreach (var (name, value) in framesDictionary)
        {
            frames.Add(ParsePlistFrame(name, ReadDictionary(value)));
        }

        string? image = null;

        if (top.TryGetValue("meta", out var metaElement))
        {
            image = ReadDictionary(metaElement).GetValueOrDefault("image")?.Value;
        }

        return new TexturePackerDocument(frames, image);
    }

    private static TexturePackerFrame ParsePlistFrame(
        string name,
        IReadOnlyDictionary<string, XElement> frame)
    {
        var rectValues = ParseNumbers(RequireValue(frame, "frame"));

        if (rectValues.Length < 4)
        {
            throw new InvalidDataException($"Frame '{name}' has an invalid frame rectangle.");
        }

        var rect = new IntRect(rectValues[0], rectValues[1], rectValues[2], rectValues[3]);
        var rotated = frame.TryGetValue("rotated", out var rotatedElement) &&
                      rotatedElement.Name.LocalName == "true";
        var originWidth = rect.Width;
        var originHeight = rect.Height;

        if (frame.TryGetValue("sourceSize", out var sourceSizeElement))
        {
            var size = ParseNumbers(sourceSizeElement.Value);
            if (size.Length >= 2)
            {
                originWidth = size[0];
                originHeight = size[1];
            }
        }

        var offsetX = 0;
        var offsetY = 0;

        if (frame.TryGetValue("offset", out var offsetElement))
        {
            var offset = ParseNumbers(offsetElement.Value);
            if (offset.Length >= 2)
            {
                offsetX = offset[0];
                offsetY = offset[1];
            }
        }
        else if (frame.TryGetValue("sourceColorRect", out var colorRectElement))
        {
            var colorRect = ParseNumbers(colorRectElement.Value);
            if (colorRect.Length >= 2)
            {
                offsetX = colorRect[0];
                offsetY = colorRect[1];
            }
        }

        return new TexturePackerFrame(
            name,
            rect,
            rotated,
            offsetX,
            offsetY,
            originWidth,
            originHeight,
            null,
            null);
    }

    private static TexturePackerDocument ParseXml(string text)
    {
        var root = XDocument.Parse(text).Root
            ?? throw new InvalidDataException("XML has no root element.");

        if (root.Name.LocalName != "TextureAtlas")
        {
            throw new InvalidDataException("XML metadata must use the TextureAtlas root element.");
        }

        var image = (string?)root.Attribute("imagePath");
        var frames = new List<TexturePackerFrame>();

        foreach (var element in root.Elements().Where(element => element.Name.LocalName == "SubTexture"))
        {
            var name = (string?)element.Attribute("name")
                ?? throw new InvalidDataException("SubTexture is missing a name.");
            var x = (int?)element.Attribute("x") ?? 0;
            var y = (int?)element.Attribute("y") ?? 0;
            var width = (int?)element.Attribute("width") ?? 0;
            var height = (int?)element.Attribute("height") ?? 0;
            var frameX = (int?)element.Attribute("frameX") ?? 0;
            var frameY = (int?)element.Attribute("frameY") ?? 0;
            var originWidth = (int?)element.Attribute("frameWidth") ?? width;
            var originHeight = (int?)element.Attribute("frameHeight") ?? height;

            frames.Add(new TexturePackerFrame(
                name,
                new IntRect(x, y, width, height),
                false,
                frameX < 0 ? -frameX : 0,
                frameY < 0 ? -frameY : 0,
                originWidth,
                originHeight,
                null,
                null));
        }

        return new TexturePackerDocument(frames, image);
    }

    private static Dictionary<string, XElement> ReadDictionary(XElement element)
    {
        if (element.Name.LocalName != "dict")
        {
            throw new InvalidDataException("Expected a plist dictionary.");
        }

        var children = element.Elements().ToArray();
        var result = new Dictionary<string, XElement>(StringComparer.Ordinal);

        for (var index = 0; index + 1 < children.Length; index += 2)
        {
            if (children[index].Name.LocalName != "key")
            {
                throw new InvalidDataException("Expected a plist key.");
            }

            result[children[index].Value] = children[index + 1];
        }

        return result;
    }

    private static string RequireValue(IReadOnlyDictionary<string, XElement> dictionary, string key) =>
        dictionary.TryGetValue(key, out var element)
            ? element.Value
            : throw new InvalidDataException($"Plist frame is missing '{key}'.");

    private static int[] ParseNumbers(string text)
    {
        var numbers = new List<int>();
        var index = 0;

        while (index < text.Length)
        {
            while (index < text.Length && !char.IsDigit(text[index]) && text[index] != '-')
            {
                index++;
            }

            if (index >= text.Length)
            {
                break;
            }

            var start = index;

            if (text[index] == '-')
            {
                index++;
            }

            while (index < text.Length && char.IsDigit(text[index]))
            {
                index++;
            }

            numbers.Add(int.Parse(text.AsSpan(start, index - start), CultureInfo.InvariantCulture));
        }

        return numbers.ToArray();
    }

    private static string NormalizeName(string name)
    {
        var normalized = name.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        var dot = normalized.LastIndexOf('.');

        if (dot > slash)
        {
            normalized = normalized[..dot];
        }

        normalized = normalized.TrimStart('/');

        if (normalized.Length == 0)
        {
            throw new InvalidDataException($"Frame name '{name}' does not produce a usable URI path.");
        }

        return normalized;
    }

    private static RgbaImage RotateCounterClockwise(RgbaImage source)
    {
        var result = new RgbaImage(source.Height, source.Width);

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var pixel = source.GetPixel(x, y);
                result.SetPixel(y, source.Width - 1 - x, pixel.Red, pixel.Green, pixel.Blue, pixel.Alpha);
            }
        }

        return result;
    }

    private sealed record TexturePackerDocument(
        IReadOnlyList<TexturePackerFrame> Frames,
        string? Image);

    private sealed record TexturePackerFrame(
        string Name,
        IntRect Rect,
        bool Rotated,
        int OffsetX,
        int OffsetY,
        int OriginWidth,
        int OriginHeight,
        double? PivotX,
        double? PivotY);
}
