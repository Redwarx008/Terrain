using System.Threading.Tasks;
using Terrain.Editor.Services;
using Terrain.Editor.Tests;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class EditorDirtyStateTests
{
    public static void RunAll()
    {
        TestHarness.Run("dirty snapshot preserves same resource dirtied again before clear", DirtySnapshotPreservesSameResourceDirtiedAgainBeforeClear);
        TestHarness.Run("dirty snapshot clears captured resource and keeps later different resource", DirtySnapshotClearsCapturedResourceAndKeepsLaterDifferentResource);
        TestHarness.Run("dirty snapshot preserves same resource dirtied again from another thread", DirtySnapshotPreservesSameResourceDirtiedAgainFromAnotherThread);
    }

    private static void DirtySnapshotPreservesSameResourceDirtiedAgainBeforeClear()
    {
        EditorDirtyState state = EditorDirtyState.Instance;
        state.ClearDirty();

        state.MarkDirty(EditorDirtyResource.Heightmap);
        EditorDirtySnapshot snapshot = state.CaptureSnapshot();
        state.MarkDirty(EditorDirtyResource.Heightmap);

        state.ClearDirty(snapshot);

        TestHarness.AssertEqual(EditorDirtyResource.Heightmap, state.DirtyResources, "same resource dirtied after snapshot should remain dirty");
        state.ClearDirty();
    }

    private static void DirtySnapshotClearsCapturedResourceAndKeepsLaterDifferentResource()
    {
        EditorDirtyState state = EditorDirtyState.Instance;
        state.ClearDirty();

        state.MarkDirty(EditorDirtyResource.Heightmap);
        EditorDirtySnapshot snapshot = state.CaptureSnapshot();
        state.MarkDirty(EditorDirtyResource.MapDefinition);

        state.ClearDirty(snapshot);

        TestHarness.AssertEqual(EditorDirtyResource.MapDefinition, state.DirtyResources, "different resource dirtied after snapshot should remain dirty");
        state.ClearDirty();
    }

    private static void DirtySnapshotPreservesSameResourceDirtiedAgainFromAnotherThread()
    {
        EditorDirtyState state = EditorDirtyState.Instance;
        state.ClearDirty();

        state.MarkDirty(EditorDirtyResource.Heightmap);
        EditorDirtySnapshot snapshot = state.CaptureSnapshot();
        Task.Run(() => state.MarkDirty(EditorDirtyResource.Heightmap)).GetAwaiter().GetResult();

        state.ClearDirty(snapshot);

        TestHarness.AssertEqual(EditorDirtyResource.Heightmap, state.DirtyResources, "same resource dirtied after snapshot on another thread should remain dirty");
        state.ClearDirty();
    }
}
