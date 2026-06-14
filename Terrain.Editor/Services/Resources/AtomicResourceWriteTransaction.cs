#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

namespace Terrain.Editor.Services.Resources;

internal sealed class AtomicResourceWriteTransaction : IDisposable
{
    private readonly List<Entry> entries = [];
    private bool committed;

    public string CreateStagingPath(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("Target path must not be null or empty.", nameof(targetPath));

        string fullTargetPath = Path.GetFullPath(targetPath);
        string directory = Path.GetDirectoryName(fullTargetPath)
            ?? throw new InvalidOperationException($"Target path '{targetPath}' does not have a parent directory.");
        string stagingPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullTargetPath)}.{Guid.NewGuid():N}.staging");

        entries.Add(new Entry(fullTargetPath, stagingPath));
        return stagingPath;
    }

    public void Commit()
    {
        if (committed)
            throw new InvalidOperationException("The transaction has already been committed.");

        var appliedEntries = new List<Entry>(entries.Count);
        try
        {
            foreach (Entry entry in entries)
            {
                string directory = Path.GetDirectoryName(entry.TargetPath)
                    ?? throw new InvalidOperationException($"Target path '{entry.TargetPath}' does not have a parent directory.");
                Directory.CreateDirectory(directory);

                if (File.Exists(entry.TargetPath))
                {
                    entry.BackupPath = Path.Combine(
                        directory,
                        $".{Path.GetFileName(entry.TargetPath)}.{Guid.NewGuid():N}.backup");
                    File.Replace(entry.StagingPath, entry.TargetPath, entry.BackupPath, ignoreMetadataErrors: true);
                    entry.State = EntryState.ReplacedExisting;
                }
                else
                {
                    File.Move(entry.StagingPath, entry.TargetPath);
                    entry.State = EntryState.CreatedNew;
                }

                appliedEntries.Add(entry);
            }

            committed = true;
        }
        catch
        {
            RollBackAppliedEntries(appliedEntries);
            throw;
        }
        finally
        {
            if (committed)
                DeleteBackups();

            CleanupStagingFiles();
        }
    }

    public void Dispose()
    {
        if (!committed)
            CleanupStagingFiles();
    }

    private void RollBackAppliedEntries(List<Entry> appliedEntries)
    {
        for (int index = appliedEntries.Count - 1; index >= 0; index--)
        {
            Entry entry = appliedEntries[index];
            try
            {
                switch (entry.State)
                {
                    case EntryState.CreatedNew:
                        if (File.Exists(entry.TargetPath))
                            File.Delete(entry.TargetPath);
                        break;
                    case EntryState.ReplacedExisting:
                        RestoreBackup(entry);
                        break;
                }
            }
            catch
            {
                // Best effort rollback; preserve the original exception.
            }
        }
    }

    private static void RestoreBackup(Entry entry)
    {
        if (entry.BackupPath == null || !File.Exists(entry.BackupPath))
            return;

        if (File.Exists(entry.TargetPath))
        {
            File.Replace(entry.BackupPath, entry.TargetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            entry.BackupPath = null;
            return;
        }

        File.Move(entry.BackupPath, entry.TargetPath);
        entry.BackupPath = null;
    }

    private void CleanupStagingFiles()
    {
        foreach (Entry entry in entries)
        {
            try
            {
                if (File.Exists(entry.StagingPath))
                    File.Delete(entry.StagingPath);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }

    private void DeleteBackups()
    {
        foreach (Entry entry in entries)
        {
            if (entry.BackupPath == null)
                continue;

            try
            {
                if (File.Exists(entry.BackupPath))
                    File.Delete(entry.BackupPath);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }

    private sealed class Entry
    {
        public Entry(string targetPath, string stagingPath)
        {
            TargetPath = targetPath;
            StagingPath = stagingPath;
        }

        public string TargetPath { get; }
        public string StagingPath { get; }
        public string? BackupPath { get; set; }
        public EntryState State { get; set; }
    }

    private enum EntryState
    {
        None = 0,
        CreatedNew = 1,
        ReplacedExisting = 2,
    }
}
