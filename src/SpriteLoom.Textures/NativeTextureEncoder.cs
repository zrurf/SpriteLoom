using System.Reflection;
using System.Runtime.InteropServices;
using SpriteLoom.Foundation;
using SpriteLoom.Imaging;

namespace SpriteLoom.Textures;

public sealed class NativeTextureEncoder : ITextureEncoder
{
    private const int ApiVersion = 1;

    private readonly AstcEncodeDelegate _astcEncode;
    private readonly BcEncodeDelegate _bcEncode;
    private readonly EtcEncodeDelegate _etcEncode;
    private readonly BasisEncodeDelegate _basisEncode;
    private readonly BufferFreeDelegate _bufferFree;

    private NativeTextureEncoder(
        AstcEncodeDelegate astcEncode,
        BcEncodeDelegate bcEncode,
        EtcEncodeDelegate etcEncode,
        BasisEncodeDelegate basisEncode,
        BufferFreeDelegate bufferFree)
    {
        _astcEncode = astcEncode;
        _bcEncode = bcEncode;
        _etcEncode = etcEncode;
        _basisEncode = basisEncode;
        _bufferFree = bufferFree;
    }

    public string Id => "native";

    public static NativeTextureEncoder? TryCreate()
    {
        if (Environment.GetEnvironmentVariable("SLOOM_DISABLE_NATIVE") == "1")
        {
            return null;
        }

        foreach (var candidate in GetCandidates())
        {
            if (candidate is null || !NativeLibrary.TryLoad(candidate, out var library))
            {
                if (candidate is null &&
                    NativeLibrary.TryLoad("sloom_native", Assembly.GetExecutingAssembly(), null, out library))
                {
                    // Fall through with the resolved default name.
                }
                else
                {
                    continue;
                }
            }

            if (TryBind(library, out var encoder))
            {
                return encoder;
            }

            NativeLibrary.Free(library);
        }

        return null;
    }

    public bool CanEncode(TextureFormat format) =>
        format.Id switch
        {
            "ktx2-astc4x4" or "ktx2-astc6x6" or "ktx2-astc8x8" or "ktx2-astc12x12" or
            "ktx2-bc1" or "ktx2-bc3" or "ktx2-bc4" or "ktx2-bc5" or "ktx2-bc7" or
            "ktx2-etc2-rgba" or "ktx2-basis-uastc" or "ktx2-basis-etc1s" => true,
            _ => false
        };

    public byte[] EncodeLevel(RgbaImage image, TextureFormat format, TextureQuality quality)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(format);

        var source = new SloomImage
        {
            Pixels = Marshal.AllocHGlobal(image.Pixels.Length),
            Width = (uint)image.Width,
            Height = (uint)image.Height,
            Stride = (uint)image.Stride,
            Format = 0
        };

        try
        {
            Marshal.Copy(image.Pixels.ToArray(), 0, source.Pixels, image.Pixels.Length);
            var result = format.Id switch
            {
                "ktx2-astc4x4" => EncodeAstc(ref source, 4, 4, quality),
                "ktx2-astc6x6" => EncodeAstc(ref source, 6, 6, quality),
                "ktx2-astc8x8" => EncodeAstc(ref source, 8, 8, quality),
                "ktx2-astc12x12" => EncodeAstc(ref source, 12, 12, quality),
                "ktx2-bc1" => EncodeBc(ref source, 1, quality),
                "ktx2-bc3" => EncodeBc(ref source, 3, quality),
                "ktx2-bc4" => EncodeBc(ref source, 4, quality),
                "ktx2-bc5" => EncodeBc(ref source, 5, quality),
                "ktx2-bc7" => EncodeBc(ref source, 7, quality),
                "ktx2-etc2-rgba" => EncodeEtc(ref source, 2, quality),
                "ktx2-basis-uastc" => EncodeBasis(ref source, 1, quality),
                "ktx2-basis-etc1s" => EncodeBasis(ref source, 0, quality),
                _ => throw new InvalidOperationException($"Native encoder does not support {format.Id}.")
            };

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(source.Pixels);
        }
    }

    public byte[] EncodeContainer(
        IReadOnlyList<RgbaImage> levels,
        TextureFormat format,
        TextureQuality quality)
    {
        if (!format.Basis)
        {
            var ktxLevels = new Ktx2Level[levels.Count];

            for (var index = 0; index < levels.Count; index++)
            {
                var level = levels[index];
                ktxLevels[index] = new Ktx2Level(
                    level.Width,
                    level.Height,
                    EncodeLevel(level, format, quality));
            }

            return Ktx2Writer.Write(
                format,
                levels[0].Width,
                levels[0].Height,
                ktxLevels);
        }

        if (levels.Count == 0)
        {
            throw new ArgumentException("At least one level is required.", nameof(levels));
        }

        return EncodeLevel(levels[0], format, quality);
    }

    private byte[] EncodeAstc(ref SloomImage source, uint blockWidth, uint blockHeight, TextureQuality quality)
    {
        var buffer = new SloomBuffer();
        var error = _astcEncode(ref source, blockWidth, blockHeight, (int)quality, ref buffer);
        return CopyBuffer(error, ref buffer);
    }

    private byte[] EncodeBc(ref SloomImage source, uint variant, TextureQuality quality)
    {
        var buffer = new SloomBuffer();
        var error = _bcEncode(ref source, variant, (int)quality, ref buffer);
        return CopyBuffer(error, ref buffer);
    }

    private byte[] EncodeEtc(ref SloomImage source, uint variant, TextureQuality quality)
    {
        var buffer = new SloomBuffer();
        var error = _etcEncode(ref source, variant, (int)quality, ref buffer);
        return CopyBuffer(error, ref buffer);
    }

    private byte[] EncodeBasis(ref SloomImage source, uint mode, TextureQuality quality)
    {
        var buffer = new SloomBuffer();
        var error = _basisEncode(ref source, mode, (int)quality, ref buffer);
        return CopyBuffer(error, ref buffer);
    }

    private byte[] CopyBuffer(int error, ref SloomBuffer buffer)
    {
        if (error != 0)
        {
            _bufferFree(ref buffer);
            throw new InvalidOperationException($"Native texture encoder failed with error {error}.");
        }

        try
        {
            var data = new byte[checked((int)buffer.Size)];
            Marshal.Copy(buffer.Data, data, 0, data.Length);
            return data;
        }
        finally
        {
            _bufferFree(ref buffer);
        }
    }

    private static bool TryBind(nint library, out NativeTextureEncoder? encoder)
    {
        encoder = null;

        if (!NativeLibrary.TryGetExport(library, "sloom_native_api_version", out var versionPointer))
        {
            return false;
        }

        var version = Marshal.GetDelegateForFunctionPointer<ApiVersionDelegate>(versionPointer)();

        if (version != ApiVersion ||
            !NativeLibrary.TryGetExport(library, "sloom_astc_encode", out var astcPointer) ||
            !NativeLibrary.TryGetExport(library, "sloom_bc_encode", out var bcPointer) ||
            !NativeLibrary.TryGetExport(library, "sloom_etc_encode", out var etcPointer) ||
            !NativeLibrary.TryGetExport(library, "sloom_basis_encode", out var basisPointer) ||
            !NativeLibrary.TryGetExport(library, "sloom_buffer_free", out var freePointer))
        {
            return false;
        }

        encoder = new NativeTextureEncoder(
            Marshal.GetDelegateForFunctionPointer<AstcEncodeDelegate>(astcPointer),
            Marshal.GetDelegateForFunctionPointer<BcEncodeDelegate>(bcPointer),
            Marshal.GetDelegateForFunctionPointer<EtcEncodeDelegate>(etcPointer),
            Marshal.GetDelegateForFunctionPointer<BasisEncodeDelegate>(basisPointer),
            Marshal.GetDelegateForFunctionPointer<BufferFreeDelegate>(freePointer));
        return true;
    }

    private static IEnumerable<string?> GetCandidates()
    {
        var explicitPath = Environment.GetEnvironmentVariable("SLOOM_NATIVE_PATH");

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            yield return explicitPath;
        }

        var fileName = OperatingSystem.IsWindows()
            ? "sloom_native.dll"
            : OperatingSystem.IsMacOS() ? "libsloom_native.dylib" : "libsloom_native.so";
        yield return Path.Combine(AppContext.BaseDirectory, fileName);
        yield return Path.Combine(
            AppContext.BaseDirectory,
            "runtimes",
            RuntimeInformation.RuntimeIdentifier,
            "native",
            fileName);
        yield return null;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint ApiVersionDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AstcEncodeDelegate(
        ref SloomImage source,
        uint blockWidth,
        uint blockHeight,
        int quality,
        ref SloomBuffer destination);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int BcEncodeDelegate(
        ref SloomImage source,
        uint variant,
        int quality,
        ref SloomBuffer destination);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EtcEncodeDelegate(
        ref SloomImage source,
        uint variant,
        int quality,
        ref SloomBuffer destination);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int BasisEncodeDelegate(
        ref SloomImage source,
        uint mode,
        int quality,
        ref SloomBuffer destination);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BufferFreeDelegate(ref SloomBuffer buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct SloomImage
    {
        public nint Pixels;
        public uint Width;
        public uint Height;
        public uint Stride;
        public uint Format;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SloomBuffer
    {
        public nint Data;
        public ulong Size;
    }
}
