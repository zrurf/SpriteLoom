using System.Text.Json;
using SpriteLoom.Codecs;
using SpriteLoom.Foundation;
using SpriteLoom.Hosting;

namespace SpriteLoom.Integration.Tests;

public sealed class TexturePackerImportTests
{
    [Theory]
    [InlineData("json")]
    [InlineData("plist")]
    [InlineData("xml")]
    public async Task Imports_texturepacker_metadata(string format)
    {
        var root = Path.Combine(Path.GetTempPath(), "sloom-tp-" + Guid.NewGuid().ToString("N"));
        var artDirectory = Path.Combine(root, "art");
        Directory.CreateDirectory(artDirectory);

        try
        {
            WriteSheet(Path.Combine(artDirectory, "sheet.png"));

            var metadataPath = Path.Combine(artDirectory, "sheet." + format);
            File.WriteAllText(metadataPath, format switch
            {
                "json" => JsonMetadata,
                "plist" => PlistMetadata,
                "xml" => XmlMetadata,
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            });

            var configPath = Path.Combine(root, "sloom.toml");
            File.WriteAllText(configPath, Config(format));

            var application = SloomApplication.Load(configPath);
            var result = await application.PackAsync();

            using var manifest = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(result.OutputPath, "manifest.json")));
            var records = manifest.RootElement.GetProperty("frames").GetProperty("records");

            Assert.Equal(1, records.GetArrayLength());
            var record = records[0];
            Assert.Equal(16, record.GetProperty("origin")[0].GetInt32());
            Assert.Equal(16, record.GetProperty("origin")[1].GetInt32());
            Assert.Equal(2, record.GetProperty("offset")[0].GetInt32());
            Assert.Equal(3, record.GetProperty("offset")[1].GetInt32());
            Assert.Equal(128, record.GetProperty("pivot")[0].GetInt32());
            Assert.Equal(128, record.GetProperty("pivot")[1].GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteSheet(string path)
    {
        var image = RgbaImage.CreateTransparent(16, 16);

        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                image.SetPixel(x, y, 180, 90, 30, 255);
            }
        }

        File.WriteAllBytes(path, new SkiaImageCodec().EncodePng(image));
    }

    private static string Config(string format) => $$"""
        schema = 1

        [project]
        id = "heroes"

        [[sources]]
        id = "sheet"
        kind = "texturepacker"
        path = "art/sheet.{{format}}"

        [[passes]]
        id = "pack"
        use = "pack.maxrects"
        params = { max-size = [32, 32], padding = 1, pow2 = false, align = 1 }

        [output]
        kind = "directory"
        path = "build/tp"
        manifest = "json"
        """;

    private const string JsonMetadata = """
        {
          "frames": {
            "a1.png": {
              "frame": { "x": 0, "y": 0, "w": 4, "h": 4 },
              "rotated": false,
              "trimmed": true,
              "spriteSourceSize": { "x": 2, "y": 3, "w": 4, "h": 4 },
              "sourceSize": { "w": 16, "h": 16 },
              "pivot": { "x": 0.5, "y": 0.5 }
            }
          },
          "meta": { "image": "sheet.png" }
        }
        """;

    private const string PlistMetadata = """
        <?xml version="1.0" encoding="UTF-8"?>
        <plist version="1.0">
        <dict>
          <key>frames</key>
          <dict>
            <key>a1.png</key>
            <dict>
              <key>frame</key><string>{{0,0},{4,4}}</string>
              <key>offset</key><string>{2,3}</string>
              <key>rotated</key><false/>
              <key>sourceSize</key><string>{16,16}</string>
              <key>sourceColorRect</key><string>{{2,3},{4,4}}</string>
            </dict>
          </dict>
          <key>meta</key>
          <dict>
            <key>image</key><string>sheet.png</string>
          </dict>
        </dict>
        </plist>
        """;

    private const string XmlMetadata = """
        <TextureAtlas imagePath="sheet.png">
          <SubTexture name="a1.png" x="0" y="0" width="4" height="4"
                      frameX="-2" frameY="-3" frameWidth="16" frameHeight="16" />
        </TextureAtlas>
        """;
}
