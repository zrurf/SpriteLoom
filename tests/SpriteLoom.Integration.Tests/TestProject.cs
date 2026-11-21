using SpriteLoom.Codecs;
using SpriteLoom.Foundation;

namespace SpriteLoom.Integration.Tests;

internal sealed class TestProject : IDisposable
{
    private readonly string _outputPath;

    private TestProject(string root, string configPath, string outputPath)
    {
        Root = root;
        ConfigPath = configPath;
        _outputPath = outputPath;
    }

    public string Root { get; }

    public string ConfigPath { get; }

    public string OutputPath => Path.Combine(Root, _outputPath);

    public static TestProject Create(
        string manifestFormat = "json",
        string outputKind = "directory",
        string outputPath = "build/heroes",
        string? signingKey = null,
        IReadOnlyList<string>? textureTargets = null,
        bool mipmaps = false,
        IReadOnlyList<string>? rasterFormats = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "sloom-it-" + Guid.NewGuid().ToString("N"));
        var runDirectory = Path.Combine(root, "art", "run");
        Directory.CreateDirectory(runDirectory);

        var codec = new SkiaImageCodec();
        WriteFrame(codec, Path.Combine(runDirectory, "a1.png"), 2, 2, 4, 4);
        WriteFrame(codec, Path.Combine(runDirectory, "a2.png"), 1, 1, 6, 6);
        WriteFrame(codec, Path.Combine(runDirectory, "a10.png"), 2, 2, 4, 4);

        var signing = signingKey is null ? string.Empty : $"[signing]\nkey = \"{signingKey}\"\n";
        var encode = string.Empty;
        var texture = string.Empty;

        if (rasterFormats is { Count: > 0 })
        {
            var formats = string.Join(", ", rasterFormats.Select(format => $"\"{format}\""));
            encode =
                "[[passes]]\n" +
                "id = \"encode\"\n" +
                "use = \"encode.page\"\n" +
                $"params = {{ formats = [{formats}], quality = 90 }}\n";
        }

        if (textureTargets is { Count: > 0 })
        {
            var targets = string.Join(", ", textureTargets.Select(target => $"\"{target}\""));
            texture =
                "[[passes]]\n" +
                "id = \"texture\"\n" +
                "use = \"texture.encode\"\n" +
                $"params = {{ targets = [{targets}], pixel-format = \"rgba8888\", dither = \"none\", " +
                $"mipmaps = {mipmaps.ToString().ToLowerInvariant()}, quality = \"balanced\" }}\n";
        }

        var configPath = Path.Combine(root, "sloom.toml");
        File.WriteAllText(
            configPath,
            Config
                .Replace("{manifest}", manifestFormat)
                .Replace("{kind}", outputKind)
                .Replace("{path}", outputPath)
                .Replace("{signing}", signing)
                .Replace("{texture}", texture)
                .Replace("{encode}", encode));

        return new TestProject(root, configPath, outputPath);
    }

    public void AddFrame(string name)
    {
        var codec = new SkiaImageCodec();
        WriteFrame(codec, Path.Combine(Root, "art", "run", name), 3, 3, 5, 5);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static void WriteFrame(
        SkiaImageCodec codec,
        string path,
        int x,
        int y,
        int width,
        int height)
    {
        var image = RgbaImage.CreateTransparent(16, 16);

        for (var row = y; row < y + height; row++)
        {
            for (var column = x; column < x + width; column++)
            {
                image.SetPixel(column, row, 200, 120, 40, 255);
            }
        }

        File.WriteAllBytes(path, codec.EncodePng(image));
    }

    private const string Config = """
        schema = 1

        [project]
        id = "heroes"
        name = "Heroes"

        [[sources]]
        id = "run"
        path = "art/run"
        order = "natural"
        frame-duration-ms = 100

        [[animations]]
        uri = "anim://heroes/run"
        source = "run"
        loop = true
        playback = "forward"

        [[passes]]
        id = "trim"
        use = "trim.fit-pixel"
        params = { threshold = 1 }

        [[passes]]
        id = "pack"
        use = "pack.maxrects"
        params = { max-size = [64, 64], padding = 1, pow2 = false, align = 1 }

        [[passes]]
        id = "bleed"
        use = "bleed.page"
        params = { extrude = 1, alpha-dilate = true }

        {texture}

        {encode}

        [output]
        kind = "{kind}"
        path = "{path}"
        manifest = "{manifest}"

        {signing}
        """;
}
