using System.Reflection;
using Terrain.Editor.Services.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class AtomicResourceWriteTransactionTests
{
    public static void RunAll()
    {
        TestHarness.Run("transaction dispose preserves backups for uncommitted work", TransactionDisposePreservesBackupsForUncommittedWork);
        TestHarness.Run("transaction restores earlier replaced files when a later commit step fails", TransactionRestoresEarlierReplacedFilesWhenLaterCommitStepFails);
    }

    private static void TransactionDisposePreservesBackupsForUncommittedWork()
    {
        string root = CreateWorkspace();
        string targetPath = Path.Combine(root, "map_data", "default.toml");
        string backupPath = Path.Combine(root, "map_data", "default.toml.test-backup");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(backupPath, "original-backup");

        var transaction = new AtomicResourceWriteTransaction();
        _ = transaction.CreateStagingPath(targetPath);
        SetFirstBackupPath(transaction, backupPath);

        transaction.Dispose();

        TestHarness.Assert(File.Exists(backupPath), "disposing an uncommitted transaction should not delete backups");
    }

    private static void TransactionRestoresEarlierReplacedFilesWhenLaterCommitStepFails()
    {
        string root = CreateWorkspace();
        string firstTarget = Path.Combine(root, "map_data", "default.toml");
        string secondTarget = Path.Combine(root, "map_data", "biome_settings.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(firstTarget)!);
        File.WriteAllText(firstTarget, "original-default");
        File.WriteAllText(secondTarget, "original-biome-settings");

        using var lockedSecondTarget = new FileStream(secondTarget, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var transaction = new AtomicResourceWriteTransaction();
        string firstStaging = transaction.CreateStagingPath(firstTarget);
        string secondStaging = transaction.CreateStagingPath(secondTarget);
        File.WriteAllText(firstStaging, "new-default");
        File.WriteAllText(secondStaging, "new-biome-settings");

        TestHarness.AssertThrows<Exception>(
            transaction.Commit,
            "commit should fail when a later target cannot be replaced");

        TestHarness.AssertEqual("original-default", File.ReadAllText(firstTarget), "earlier replaced target should be restored");
        TestHarness.AssertEqual("original-biome-settings", File.ReadAllText(secondTarget), "failing target should remain unchanged");
        TestHarness.Assert(
            !Directory.EnumerateFiles(Path.Combine(root, "map_data"), "*.backup", SearchOption.TopDirectoryOnly).Any(),
            "successful rollback should clean transaction backup files");
    }

    private static void SetFirstBackupPath(AtomicResourceWriteTransaction transaction, string backupPath)
    {
        FieldInfo entriesField = typeof(AtomicResourceWriteTransaction)
            .GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("entries field not found");
        var entries = (System.Collections.IList?)entriesField.GetValue(transaction)
            ?? throw new InvalidOperationException("entries list not found");
        object firstEntry = entries[0]
            ?? throw new InvalidOperationException("transaction entry not found");
        PropertyInfo backupPathProperty = firstEntry.GetType()
            .GetProperty("BackupPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BackupPath property not found");
        backupPathProperty.SetValue(firstEntry, backupPath);
    }

    private static string CreateWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-atomic-transaction-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
