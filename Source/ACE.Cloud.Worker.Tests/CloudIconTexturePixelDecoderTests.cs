using ACE.Cloud.Domain;
using ACE.Cloud.Worker;
using ACE.Entity.Enum;

namespace ACE.Cloud.Worker.Tests;

/// <summary>
/// Synthetic (no real DAT bytes) Red/Green coverage for ASSET-004's "Test missing, corrupt,
/// unsupported, oversized, and malicious references" requirement, exercising every pixel format
/// Icon Reconstruction supports plus every rejection path, entirely from hand-built byte arrays.
/// </summary>
[TestClass]
public sealed class CloudIconTexturePixelDecoderTests
{
    private static readonly CloudIconTexturePixelDecoder.PaletteBytesResolver NoPalettes = (_, _) => Task.FromResult<byte[]?>(null);

    [TestMethod]
    public async Task DecodeAsync_A8R8G8B8_DecodesEachPixelInFileOrder()
    {
        // 1x1, file bytes are B,G,R,A.
        var bytes = BuildTexture(1, 1, SurfacePixelFormat.PFID_A8R8G8B8, [0x30, 0x20, 0x10, 0xFF]);

        var resolution = await CloudIconTexturePixelDecoder.DecodeAsync(bytes, [], NoPalettes);

        Assert.AreEqual(CloudIconLayerResolutionOutcomeKind.Resolved, resolution.Outcome);
        CollectionAssert.AreEqual(new byte[] { 0x10, 0x20, 0x30, 0xFF }, resolution.Raster!.Rgba);
    }

    [TestMethod]
    public async Task DecodeAsync_R8G8B8_DefaultsToFullyOpaque()
    {
        var bytes = BuildTexture(1, 1, SurfacePixelFormat.PFID_R8G8B8, [0x30, 0x20, 0x10]);

        var resolution = await CloudIconTexturePixelDecoder.DecodeAsync(bytes, [], NoPalettes);

        Assert.AreEqual(CloudIconLayerResolutionOutcomeKind.Resolved, resolution.Outcome);
        CollectionAssert.AreEqual(new byte[] { 0x10, 0x20, 0x30, 0xFF }, resolution.Raster!.Rgba);
    }

    [TestMethod]
    public async Task DecodeAsync_P8Indexed_MapsThroughResolvedPalette()
    {
        const uint paletteDid = 0x04000001;
        var paletteBytes = BuildPalette(paletteDid, [0xFFAABBCC, 0xFF010203]);
        var textureBytes = BuildTexture(1, 1, SurfacePixelFormat.PFID_P8, [1], defaultPaletteId: paletteDid);

        var resolution = await CloudIconTexturePixelDecoder.DecodeAsync(
            textureBytes, [], (did, _) => Task.FromResult<byte[]?>(did == paletteDid ? paletteBytes : null));

        Assert.AreEqual(CloudIconLayerResolutionOutcomeKind.Resolved, resolution.Outcome);
        CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, 0x03, 0xFF }, resolution.Raster!.Rgba);
    }

    [TestMethod]
    public async Task DecodeAsync_IndexedWithPaletteOverride_SubstitutesOnlyTheOverriddenRange()
    {
        const uint basePaletteDid = 0x04000001;
        const uint overridePaletteDid = 0x04000002;
        var basePalette = BuildPalette(basePaletteDid, [0xFF000000, 0xFF111111, 0xFF222222]);
        var overridePalette = BuildPalette(overridePaletteDid, [0xFF000000, 0xFF999999, 0xFF222222]);
        // 2 pixels: index 1 (overridden) and index 2 (not overridden).
        var textureBytes = BuildTexture(2, 1, SurfacePixelFormat.PFID_P8, [1, 2], defaultPaletteId: basePaletteDid);

        var resolver = (uint did, CancellationToken _) => Task.FromResult<byte[]?>(
            did == basePaletteDid ? basePalette : did == overridePaletteDid ? overridePalette : null);

        var overrides = new[] { new CloudIconPaletteRangeOverride(overridePaletteDid, offset: 1, numColors: 1) };
        var resolution = await CloudIconTexturePixelDecoder.DecodeAsync(textureBytes, overrides, (did, ct) => resolver(did, ct));

        Assert.AreEqual(CloudIconLayerResolutionOutcomeKind.Resolved, resolution.Outcome);
        CollectionAssert.AreEqual(
            new byte[] { 0x99, 0x99, 0x99, 0xFF, 0x22, 0x22, 0x22, 0xFF }, resolution.Raster!.Rgba);
    }

    [TestMethod]
    public async Task DecodeAsync_PaletteIndexBeyondPaletteLength_ReturnsMalicious()
    {
        var paletteBytes = BuildPalette(0x04000001, [0xFF000000]); // only 1 color
        var textureBytes = BuildTexture(1, 1, SurfacePixelFormat.PFID_P8, [200], defaultPaletteId: 0x04000001); // index 200

        var resolution = await CloudIconTexturePixelDecoder.DecodeAsync(
            textureBytes, [], (_, _) => Task.FromResult<byte[]?>(paletteBytes));

        Assert.AreEqual(CloudIconLayerResolutionOutcomeKind.Malicious, resolution.Outcome);
    }

    [TestMethod]
    public async Task DecodeAsync_PaletteOverrideRangeBeyondPaletteLength_ReturnsMalicious()
    {
        var basePalette = BuildPalette(0x04000001, [0xFF000000, 0xFF111111]);
        var overridePalette = BuildPalette(0x04000002, [0xFF000000]); // too short for the requested range
        var textureBytes = BuildTexture(1, 1, SurfacePixelFormat.PFID_P8, [0], defaultPaletteId: 0x04000001);

        var overrides = new[] { new CloudIconPaletteRangeOverride(0x04000002, offset: 0, numColors: 2) };
        var resolution = await CloudIconTexturePixelDecoder.DecodeAsync(
            textureBytes, overrides,
            (did, _) => Task.FromResult<byte[]?>(did == 0x04000001 ? basePalette : overridePalette));

        Assert.AreEqual(CloudIconLayerResolutionOutcomeKind.Malicious, resolution.Outcome);
    }

    [TestMethod]
    public async Task DecodeAsync_IndexedTextureWhosePaletteCannotBeResolved_ReturnsCorrupt()
    {
        var textureBytes = BuildTexture(1, 1, SurfacePixelFormat.PFID_P8, [0], defaultPaletteId: 0x04000099);

        var resolution = await CloudIconTexturePixelDecoder.DecodeAsync(textureBytes, [], NoPalettes);

        Assert.AreEqual(CloudIconLayerResolutionOutcomeKind.Corrupt, resolution.Outcome);
    }

    [TestMethod]
    public async Task DecodeAsync_TruncatedSourceData_ReturnsCorrupt()
    {
        // Declares 4 bytes of A8R8G8B8 source data (1x1) but only provides 2.
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x06000001u); // Id
            writer.Write(0); // Unknown
            writer.Write(1); // Width
            writer.Write(1); // Height
            writer.Write((uint)SurfacePixelFormat.PFID_A8R8G8B8);
            writer.Write(4); // declared Length
            writer.Write(new byte[] { 1, 2 }); // only 2 bytes actually present
        }

        var resolution = await CloudIconTexturePixelDecoder.DecodeAsync(stream.ToArray(), [], NoPalettes);

        Assert.AreEqual(CloudIconLayerResolutionOutcomeKind.Corrupt, resolution.Outcome);
    }

    [TestMethod]
    public async Task DecodeAsync_NonPositiveDimensions_ReturnsCorrupt()
    {
        var bytes = BuildTexture(0, 1, SurfacePixelFormat.PFID_A8R8G8B8, []);

        var resolution = await CloudIconTexturePixelDecoder.DecodeAsync(bytes, [], NoPalettes);

        Assert.AreEqual(CloudIconLayerResolutionOutcomeKind.Corrupt, resolution.Outcome);
    }

    [TestMethod]
    public async Task DecodeAsync_DimensionsBeyondMaxRasterSize_ReturnsOversized()
    {
        var oversized = CloudIconRasterLayer.MaxDimension + 1;
        var bytes = BuildTexture(oversized, 1, SurfacePixelFormat.PFID_A8R8G8B8, new byte[oversized * 4]);

        var resolution = await CloudIconTexturePixelDecoder.DecodeAsync(bytes, [], NoPalettes);

        Assert.AreEqual(CloudIconLayerResolutionOutcomeKind.Oversized, resolution.Outcome);
    }

    [TestMethod]
    public async Task DecodeAsync_Dxt1Format_ReturnsUnsupported()
    {
        var bytes = BuildTexture(4, 4, SurfacePixelFormat.PFID_DXT1, new byte[8]);

        var resolution = await CloudIconTexturePixelDecoder.DecodeAsync(bytes, [], NoPalettes);

        Assert.AreEqual(CloudIconLayerResolutionOutcomeKind.Unsupported, resolution.Outcome);
    }

    private static byte[] BuildTexture(
        int width, int height, SurfacePixelFormat format, byte[] sourceData, uint? defaultPaletteId = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x06000001u); // Id
            writer.Write(0); // Unknown
            writer.Write(width);
            writer.Write(height);
            writer.Write((uint)format);
            writer.Write(sourceData.Length); // Length
            writer.Write(sourceData);

            if (format is SurfacePixelFormat.PFID_INDEX16 or SurfacePixelFormat.PFID_P8)
            {
                writer.Write(defaultPaletteId ?? 0u);
            }
        }

        return stream.ToArray();
    }

    private static byte[] BuildPalette(uint id, uint[] argbColors)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(id);
            writer.Write(argbColors.Length); // List<uint>.Unpack reads an Int32 count
            foreach (var color in argbColors)
            {
                writer.Write(color);
            }
        }

        return stream.ToArray();
    }
}
