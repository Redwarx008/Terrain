using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class RuntimeBiomeMaskReaderTests
{
    public static void RunAll()
    {
        TestHarness.Run("runtime biome mask reader loads fixed png bytes", RuntimeBiomeMaskReaderLoadsFixedPngBytes);
    }

    private static void RuntimeBiomeMaskReaderLoadsFixedPngBytes()
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-runtime-biome-mask-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "biome_mask.png");
        using (var image = new Image<L8>(2, 2))
        {
            image[0, 0] = new L8(1);
            image[1, 0] = new L8(2);
            image[0, 1] = new L8(3);
            image[1, 1] = new L8(4);
            image.SaveAsPng(path);
        }

        RuntimeBiomeMaskData mask = RuntimeBiomeMaskReader.ReadFrom(path);

        TestHarness.AssertEqual(2, mask.Width, "mask width");
        TestHarness.AssertEqual(2, mask.Height, "mask height");
        TestHarness.Assert(mask.Data.SequenceEqual(new byte[] { 1, 2, 3, 4 }), "mask data should preserve L8 values");
    }
}
