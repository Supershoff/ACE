using ACE.Cloud.Domain;
using ACE.DatLoader.FileTypes;
using ACE.Entity.Enum;

namespace ACE.Cloud.Worker;

/// <summary>
/// Decodes one raw, manifest-extracted <see cref="Texture"/> DAT entry into a
/// <see cref="CloudIconRasterLayer"/> without ever calling <see cref="Texture.GetBitmap"/> (that
/// method's indexed-format branch reads the palette through ACE's process-wide <c>DatManager</c>
/// singleton, which this pipeline must never touch -- see <c>PortalDatAssetExtractor</c>'s own doc
/// comment) and without any dependency on <c>System.Drawing</c>. Reuses <see cref="Texture.Unpack"/>,
/// <see cref="Palette.Unpack"/>, and <see cref="PaletteSet.Unpack"/> for binary parsing (all three are
/// pure, singleton-free methods) but reimplements pixel-to-color mapping locally, resolving any
/// indexed format's palette bytes through the caller-supplied <paramref name="paletteResolver"/>
/// instead. Every failure path classifies its <see cref="CloudIconLayerResolutionOutcomeKind"/>
/// explicitly (ASSET-004: "Test missing, corrupt, unsupported, oversized, and malicious references")
/// and never allocates a decode buffer sized from untrusted header values before bounding them.
/// </summary>
public static class CloudIconTexturePixelDecoder
{
    private static readonly HashSet<SurfacePixelFormat> SupportedFormats =
    [
        SurfacePixelFormat.PFID_A8R8G8B8,
        SurfacePixelFormat.PFID_R8G8B8,
        SurfacePixelFormat.PFID_CUSTOM_LSCAPE_R8G8B8,
        SurfacePixelFormat.PFID_A8,
        SurfacePixelFormat.PFID_CUSTOM_LSCAPE_ALPHA,
        SurfacePixelFormat.PFID_R5G6B5,
        SurfacePixelFormat.PFID_A4R4G4B4,
        SurfacePixelFormat.PFID_INDEX16,
        SurfacePixelFormat.PFID_P8,
    ];

    /// <summary>Resolves a Palette (0x04) DID to its raw manifest-extracted bytes, or null if absent.</summary>
    public delegate Task<byte[]?> PaletteBytesResolver(uint paletteDid, CancellationToken cancellationToken);

    public static async Task<CloudIconLayerResolution> DecodeAsync(
        byte[] rawTextureBytes,
        IReadOnlyList<CloudIconPaletteRangeOverride> paletteOverrides,
        PaletteBytesResolver paletteResolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rawTextureBytes);
        ArgumentNullException.ThrowIfNull(paletteOverrides);
        ArgumentNullException.ThrowIfNull(paletteResolver);

        Texture texture;
        try
        {
            texture = new Texture();
            using var reader = new BinaryReader(new MemoryStream(rawTextureBytes, writable: false));
            texture.Unpack(reader);
        }
        catch (Exception ex) when (IsParseFailure(ex))
        {
            return CloudIconLayerResolution.Failed(CloudIconLayerResolutionOutcomeKind.Corrupt);
        }

        if (texture.Width <= 0 || texture.Height <= 0)
        {
            return CloudIconLayerResolution.Failed(CloudIconLayerResolutionOutcomeKind.Corrupt);
        }

        if (texture.Width > CloudIconRasterLayer.MaxDimension || texture.Height > CloudIconRasterLayer.MaxDimension)
        {
            return CloudIconLayerResolution.Failed(CloudIconLayerResolutionOutcomeKind.Oversized);
        }

        if (!SupportedFormats.Contains(texture.Format))
        {
            return CloudIconLayerResolution.Failed(CloudIconLayerResolutionOutcomeKind.Unsupported);
        }

        int requiredSourceBytes;
        try
        {
            requiredSourceBytes = checked(texture.Width * texture.Height * BytesPerSourcePixel(texture.Format));
        }
        catch (OverflowException)
        {
            return CloudIconLayerResolution.Failed(CloudIconLayerResolutionOutcomeKind.Malicious);
        }

        if (texture.SourceData is null || texture.SourceData.Length < requiredSourceBytes)
        {
            return CloudIconLayerResolution.Failed(CloudIconLayerResolutionOutcomeKind.Corrupt);
        }

        try
        {
            var rgba = texture.Format switch
            {
                SurfacePixelFormat.PFID_A8R8G8B8 => DecodeA8R8G8B8(texture),
                SurfacePixelFormat.PFID_R8G8B8 => DecodeR8G8B8(texture, sourceOrderIsBgr: true),
                SurfacePixelFormat.PFID_CUSTOM_LSCAPE_R8G8B8 => DecodeR8G8B8(texture, sourceOrderIsBgr: false),
                SurfacePixelFormat.PFID_A8 or SurfacePixelFormat.PFID_CUSTOM_LSCAPE_ALPHA => DecodeGrayscale(texture),
                SurfacePixelFormat.PFID_R5G6B5 => DecodeR5G6B5(texture),
                SurfacePixelFormat.PFID_A4R4G4B4 => DecodeA4R4G4B4(texture),
                SurfacePixelFormat.PFID_INDEX16 or SurfacePixelFormat.PFID_P8
                    => await DecodeIndexedAsync(texture, paletteOverrides, paletteResolver, cancellationToken),
                _ => throw new InvalidOperationException("Unreachable: format already validated as supported."),
            };

            return CloudIconLayerResolution.Resolved(new CloudIconRasterLayer(texture.Width, texture.Height, rgba));
        }
        catch (CloudIconLayerCorruptTextureException)
        {
            return CloudIconLayerResolution.Failed(CloudIconLayerResolutionOutcomeKind.Corrupt);
        }
        catch (CloudIconLayerMaliciousTextureException)
        {
            return CloudIconLayerResolution.Failed(CloudIconLayerResolutionOutcomeKind.Malicious);
        }
    }

    private static bool IsParseFailure(Exception ex) =>
        ex is EndOfStreamException or IOException or ArgumentOutOfRangeException or OverflowException or ArgumentException;

    private static int BytesPerSourcePixel(SurfacePixelFormat format) => format switch
    {
        SurfacePixelFormat.PFID_A8R8G8B8 => 4,
        SurfacePixelFormat.PFID_R8G8B8 or SurfacePixelFormat.PFID_CUSTOM_LSCAPE_R8G8B8 => 3,
        SurfacePixelFormat.PFID_A8 or SurfacePixelFormat.PFID_CUSTOM_LSCAPE_ALPHA or SurfacePixelFormat.PFID_P8 => 1,
        SurfacePixelFormat.PFID_R5G6B5 or SurfacePixelFormat.PFID_A4R4G4B4 or SurfacePixelFormat.PFID_INDEX16 => 2,
        _ => throw new InvalidOperationException("Unreachable: format already validated as supported."),
    };

    private static byte[] DecodeA8R8G8B8(Texture texture)
    {
        var rgba = new byte[texture.Width * texture.Height * 4];
        for (var pixel = 0; pixel < texture.Width * texture.Height; pixel++)
        {
            var src = pixel * 4;
            var dst = pixel * 4;
            // File order is B,G,R,A (little-endian A8R8G8B8).
            rgba[dst] = texture.SourceData[src + 2];
            rgba[dst + 1] = texture.SourceData[src + 1];
            rgba[dst + 2] = texture.SourceData[src];
            rgba[dst + 3] = texture.SourceData[src + 3];
        }

        return rgba;
    }

    private static byte[] DecodeR8G8B8(Texture texture, bool sourceOrderIsBgr)
    {
        var rgba = new byte[texture.Width * texture.Height * 4];
        for (var pixel = 0; pixel < texture.Width * texture.Height; pixel++)
        {
            var src = pixel * 3;
            var dst = pixel * 4;
            if (sourceOrderIsBgr)
            {
                rgba[dst] = texture.SourceData[src + 2];
                rgba[dst + 1] = texture.SourceData[src + 1];
                rgba[dst + 2] = texture.SourceData[src];
            }
            else
            {
                rgba[dst] = texture.SourceData[src];
                rgba[dst + 1] = texture.SourceData[src + 1];
                rgba[dst + 2] = texture.SourceData[src + 2];
            }

            rgba[dst + 3] = 255;
        }

        return rgba;
    }

    private static byte[] DecodeGrayscale(Texture texture)
    {
        var rgba = new byte[texture.Width * texture.Height * 4];
        for (var pixel = 0; pixel < texture.Width * texture.Height; pixel++)
        {
            var value = texture.SourceData[pixel];
            var dst = pixel * 4;
            rgba[dst] = value;
            rgba[dst + 1] = value;
            rgba[dst + 2] = value;
            rgba[dst + 3] = 255;
        }

        return rgba;
    }

    private static byte[] DecodeR5G6B5(Texture texture)
    {
        var rgba = new byte[texture.Width * texture.Height * 4];
        for (var pixel = 0; pixel < texture.Width * texture.Height; pixel++)
        {
            var value = (ushort)(texture.SourceData[pixel * 2] | (texture.SourceData[(pixel * 2) + 1] << 8));

            var r5 = (value & 0xF800) >> 11;
            var g6 = (value & 0x07E0) >> 5;
            var b5 = value & 0x001F;

            // Expand by bit replication, not a bare shift: a plain v << 3 maps 0..31 onto
            // 0,8,...,248 and never reaches 255, so a fully saturated white would decode dim.
            var dst = pixel * 4;
            rgba[dst] = (byte)((r5 << 3) | (r5 >> 2));
            rgba[dst + 1] = (byte)((g6 << 2) | (g6 >> 4));
            rgba[dst + 2] = (byte)((b5 << 3) | (b5 >> 2));
            rgba[dst + 3] = 255;
        }

        return rgba;
    }

    private static byte[] DecodeA4R4G4B4(Texture texture)
    {
        var rgba = new byte[texture.Width * texture.Height * 4];
        for (var pixel = 0; pixel < texture.Width * texture.Height; pixel++)
        {
            var value = (ushort)(texture.SourceData[pixel * 2] | (texture.SourceData[(pixel * 2) + 1] << 8));

            var a = (value >> 12) & 0xF;
            var r = (value >> 8) & 0xF;
            var g = (value >> 4) & 0xF;
            var b = value & 0xF;

            // 255 / 15 == 17 exactly, so replicating the nibble maps 0..15 onto 0..255 losslessly.
            var dst = pixel * 4;
            rgba[dst] = (byte)(r * 17);
            rgba[dst + 1] = (byte)(g * 17);
            rgba[dst + 2] = (byte)(b * 17);
            rgba[dst + 3] = (byte)(a * 17);
        }

        return rgba;
    }

    private static async Task<byte[]> DecodeIndexedAsync(
        Texture texture,
        IReadOnlyList<CloudIconPaletteRangeOverride> paletteOverrides,
        PaletteBytesResolver paletteResolver,
        CancellationToken cancellationToken)
    {
        if (texture.DefaultPaletteId is not { } defaultPaletteId || defaultPaletteId == 0)
        {
            throw new CloudIconLayerCorruptTextureException();
        }

        var palette = await ResolvePaletteAsync(defaultPaletteId, paletteResolver, cancellationToken);
        var colors = palette.Colors.ToArray();

        foreach (var range in paletteOverrides)
        {
            var overridePalette = await ResolvePaletteAsync(range.PaletteDid, paletteResolver, cancellationToken);

            var end = range.Offset + range.NumColors;
            if (end > colors.Length || end > overridePalette.Colors.Count)
            {
                // A crafted or mismatched sub-palette range naming indices beyond either palette's
                // actual color count: trusting it would read out of bounds of real AC data.
                throw new CloudIconLayerMaliciousTextureException();
            }

            for (var i = range.Offset; i < end; i++)
            {
                colors[i] = overridePalette.Colors[i];
            }
        }

        var isTwoByteIndex = texture.Format == SurfacePixelFormat.PFID_INDEX16;
        var rgba = new byte[texture.Width * texture.Height * 4];

        for (var pixel = 0; pixel < texture.Width * texture.Height; pixel++)
        {
            var index = isTwoByteIndex
                ? texture.SourceData[pixel * 2] | (texture.SourceData[(pixel * 2) + 1] << 8)
                : texture.SourceData[pixel];

            if (index < 0 || index >= colors.Length)
            {
                // A palette index the resolved palette cannot possibly cover: trusting it would read
                // out of bounds of real AC data, exactly the "malicious reference" scenario ASSET-004
                // asks to be tested and rejected before any such read happens.
                throw new CloudIconLayerMaliciousTextureException();
            }

            var argb = colors[index];
            var dst = pixel * 4;
            rgba[dst] = (byte)((argb >> 16) & 0xFF);
            rgba[dst + 1] = (byte)((argb >> 8) & 0xFF);
            rgba[dst + 2] = (byte)(argb & 0xFF);
            rgba[dst + 3] = (byte)((argb >> 24) & 0xFF);
        }

        return rgba;
    }

    private static async Task<Palette> ResolvePaletteAsync(uint paletteDid, PaletteBytesResolver paletteResolver, CancellationToken cancellationToken)
    {
        var bytes = await paletteResolver(paletteDid, cancellationToken) ?? throw new CloudIconLayerCorruptTextureException();

        try
        {
            var palette = new Palette();
            using var reader = new BinaryReader(new MemoryStream(bytes, writable: false));
            palette.Unpack(reader);
            return palette;
        }
        catch (Exception ex) when (IsParseFailure(ex))
        {
            throw new CloudIconLayerCorruptTextureException();
        }
    }

    private sealed class CloudIconLayerCorruptTextureException : Exception;

    private sealed class CloudIconLayerMaliciousTextureException : Exception;
}
