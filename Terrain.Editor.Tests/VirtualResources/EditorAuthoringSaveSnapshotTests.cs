using System.Runtime.CompilerServices;
using Terrain.Editor.Services;
using Terrain.Editor.Services.Resources;
using Terrain.Editor.Tests;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class EditorAuthoringSaveSnapshotTests
{
    public static void RunAll()
    {
        TestHarness.Run("authoring map snapshot skips unrelated resource payloads", AuthoringMapSnapshotSkipsUnrelatedResourcePayloads);
        TestHarness.Run("authoring biome settings snapshot includes descriptor when material ids are generated", AuthoringBiomeSettingsSnapshotIncludesDescriptorWhenMaterialIdsAreGenerated);
    }

    private static void AuthoringMapSnapshotSkipsUnrelatedResourcePayloads()
    {
        var manager = (TerrainManager)RuntimeHelpers.GetUninitializedObject(typeof(TerrainManager));

        EditorAuthoringSaveSnapshot snapshot = manager.CreateAuthoringSaveSnapshot(
            riverMaxVisibleCameraHeight: 875.0f,
            seaLevel: 9.25f,
            progress: null,
            dirtySnapshot: EditorDirtySnapshot.Unversioned(EditorDirtyResource.MapDefinition));

        TestHarness.AssertEqual(EditorDirtyResource.MapDefinition, snapshot.DirtyResources, "snapshot dirty resources");
        TestHarness.Assert(snapshot.HeightData == null, "map-only snapshot should not clone height data");
        TestHarness.Assert(snapshot.BiomeMask == null, "map-only snapshot should not clone biome mask data");
        TestHarness.Assert(snapshot.DescriptorSlots == null, "map-only snapshot should not build material descriptor slots");
        TestHarness.Assert(snapshot.BiomeSnapshot == null, "map-only snapshot should not build biome settings");
        TestHarness.AssertEqual(875.0f, snapshot.RiverMaxVisibleCameraHeight, "snapshot should keep map camera visibility setting");
        TestHarness.AssertEqual(9.25f, snapshot.SeaLevel, "snapshot should keep map sea level setting");
    }

    private static void AuthoringBiomeSettingsSnapshotIncludesDescriptorWhenMaterialIdsAreGenerated()
    {
        MaterialSlotManager manager = MaterialSlotManager.Instance;
        BiomeRuleService service = BiomeRuleService.Instance;
        manager.ClearAll();
        service.ClearAll();

        try
        {
            manager[5].Name = "Soft Grass";
            manager[5].AlbedoTexturePath = "grass.png";

            service.AddBiomeFromConfig(1, "Temperate", new System.Numerics.Vector4(0, 1, 0, 1));
            service.AddLayerFromConfig(
                biomeId: 1,
                name: "Grass Layer",
                enabled: true,
                minAltitude: 0.0f,
                maxAltitude: 100.0f,
                minSlopeDegrees: 0.0f,
                maxSlopeDegrees: 45.0f,
                blendRange: 1.0f,
                materialSlotIndex: 5);

            var terrainManager = (TerrainManager)RuntimeHelpers.GetUninitializedObject(typeof(TerrainManager));

            EditorAuthoringSaveSnapshot snapshot = terrainManager.CreateAuthoringSaveSnapshot(
                riverMaxVisibleCameraHeight: 875.0f,
                seaLevel: 9.25f,
                progress: null,
                dirtySnapshot: EditorDirtySnapshot.Unversioned(EditorDirtyResource.BiomeSettings));

            TestHarness.AssertEqual(
                EditorDirtyResource.MaterialDescriptor | EditorDirtyResource.BiomeSettings,
                snapshot.DirtyResources,
                "settings-only snapshot should include descriptor when material ids are generated");
            TestHarness.Assert(snapshot.DescriptorSlots != null, "descriptor slots should be captured when generated ids must be persisted");
            TestHarness.Assert(snapshot.BiomeSnapshot != null, "biome settings should still be captured");
            TestHarness.AssertEqual("soft_grass", snapshot.DescriptorSlots![0].Id, "generated descriptor id");
            TestHarness.AssertEqual("soft_grass", snapshot.BiomeSnapshot!.Layers[0].MaterialId, "biome layer should reference persisted descriptor id");
            TestHarness.Assert(manager[5].MaterialId == null, "snapshot capture should not mutate live slot ids before the save commits");
        }
        finally
        {
            service.ClearAll();
            manager.ClearAll();
        }
    }
}
