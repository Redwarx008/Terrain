using Stride.Graphics;
using Terrain.Editor.Services;
using Terrain.Editor.Services.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class MaterialSlotManagerFallbackTextTests
{
    public static void RunAll()
    {
        TestHarness.Run("authoring mapper skips runtime fallback slots during export", AuthoringMapperSkipsRuntimeFallbackSlotsDuringExport);
        TestHarness.Run("texture block encoder builds magenta solid color mip data", TextureBlockEncoderBuildsMagentaSolidColorMipData);
        TestHarness.Run("texture block encoder zeroes bc1 magenta selector bytes", TextureBlockEncoderZeroesBc1MagentaSelectorBytes);
        TestHarness.Run("texture block encoder zeroes bc3 magenta selector bytes", TextureBlockEncoderZeroesBc3MagentaSelectorBytes);
        TestHarness.Run("normal fallback signature exists when only runtime fallback slots remain", NormalFallbackSignatureExistsWhenOnlyRuntimeFallbackSlotsRemain);
    }

    private static void AuthoringMapperSkipsRuntimeFallbackSlotsDuringExport()
    {
        string root = CreateWorkspace();
        var slots = new[]
        {
            new MaterialSlot
            {
                Index = 2,
                MaterialId = "grass",
                Name = "Grass",
                AlbedoTexturePath = Path.Combine(root, "materials", "grass.png"),
            },
            new MaterialSlot
            {
                Index = 3,
                MaterialId = "missing_rock",
                Name = "Missing:rock",
                IsRuntimeFallbackPlaceholder = true,
                UsesFallbackAlbedo = true,
                UsesFallbackNormal = true,
            },
        };

        IReadOnlyList<EditorMaterialDescriptorSlot> exported = EditorAuthoringResourceMapper.CreateMaterialDescriptorSlots(slots);

        TestHarness.AssertEqual(1, exported.Count, "runtime fallback placeholder should not be exported");
        TestHarness.AssertEqual("grass", exported[0].Id, "real authoring slot should still export");
    }

    private static void TextureBlockEncoderBuildsMagentaSolidColorMipData()
    {
        byte[][] mipData = Terrain.Utilities.TextureBlockEncoder.CreateSolidColorMipData(
            PixelFormat.R8G8B8A8_UNorm,
            width: 4,
            height: 4,
            mipCount: 1,
            r: 255,
            g: 0,
            b: 255,
            a: 255);

        TestHarness.AssertEqual(1, mipData.Length, "expected one mip level");
        byte[] pixels = mipData[0];
        TestHarness.AssertEqual(4 * 4 * 4, pixels.Length, "rgba texture byte count");
        for (int index = 0; index < pixels.Length; index += 4)
        {
            TestHarness.AssertEqual((byte)255, pixels[index], "magenta red channel");
            TestHarness.AssertEqual((byte)0, pixels[index + 1], "magenta green channel");
            TestHarness.AssertEqual((byte)255, pixels[index + 2], "magenta blue channel");
            TestHarness.AssertEqual((byte)255, pixels[index + 3], "magenta alpha channel");
        }
    }

    private static void TextureBlockEncoderZeroesBc1MagentaSelectorBytes()
    {
        PolluteStack();

        byte[][] mipData = Terrain.Utilities.TextureBlockEncoder.CreateSolidColorMipData(
            PixelFormat.BC1_UNorm,
            width: 4,
            height: 4,
            mipCount: 1,
            r: 255,
            g: 0,
            b: 255,
            a: 255);

        byte[] expected = [0x1F, 0xF8, 0x1F, 0xF8, 0x00, 0x00, 0x00, 0x00];
        AssertByteSequence(expected, mipData[0], "bc1 magenta block bytes");
    }

    private static void TextureBlockEncoderZeroesBc3MagentaSelectorBytes()
    {
        PolluteStack();

        byte[][] mipData = Terrain.Utilities.TextureBlockEncoder.CreateSolidColorMipData(
            PixelFormat.BC3_UNorm,
            width: 4,
            height: 4,
            mipCount: 1,
            r: 255,
            g: 0,
            b: 255,
            a: 255);

        byte[] expected = [0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1F, 0xF8, 0x1F, 0xF8, 0x00, 0x00, 0x00, 0x00];
        AssertByteSequence(expected, mipData[0], "bc3 magenta block bytes");
    }

    private static void NormalFallbackSignatureExistsWhenOnlyRuntimeFallbackSlotsRemain()
    {
        var slots = new[]
        {
            new MaterialSlot
            {
                Index = 0,
                MaterialId = "missing_rock",
                Name = "Missing:rock",
                IsRuntimeFallbackPlaceholder = true,
                UsesFallbackAlbedo = true,
                UsesFallbackNormal = true,
            },
        };

        MaterialSlotManager.TextureSignature? signature = MaterialSlotManager.ResolveNormalArraySignature(slots);

        TestHarness.Assert(signature.HasValue, "runtime fallback slots should still force a normal-array signature");
        MaterialSlotManager.TextureSignature resolvedSignature = signature.GetValueOrDefault();
        TestHarness.AssertEqual(64, resolvedSignature.Width, "fallback normal width");
        TestHarness.AssertEqual(64, resolvedSignature.Height, "fallback normal height");
        TestHarness.AssertEqual(PixelFormat.R8G8B8A8_UNorm, resolvedSignature.Format, "fallback normal format");
        TestHarness.AssertEqual(1, resolvedSignature.MipLevelCount, "fallback normal mip count");
    }

    private static string CreateWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-material-slot-fallback-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void PolluteStack()
    {
        Span<byte> scratch = stackalloc byte[64];
        scratch.Fill(0x7B);
    }

    private static void AssertByteSequence(IReadOnlyList<byte> expected, IReadOnlyList<byte> actual, string message)
    {
        TestHarness.AssertEqual(expected.Count, actual.Count, $"{message} length");
        for (int index = 0; index < expected.Count; index++)
        {
            TestHarness.AssertEqual(expected[index], actual[index], $"{message} byte {index}");
        }
    }
}
