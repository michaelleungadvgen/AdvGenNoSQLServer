// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.IO;

/// <summary>
/// Writes files via temp + fsync + atomic rename so a crash never leaves a
/// torn file at the final path. On Linux the parent directory is also fsynced
/// so the rename itself is durable across power loss.
/// </summary>
public static class AtomicFileWriter
{
    /// <summary>
    /// Asynchronously writes text to a file atomically via temp file, fsync and rename; cleans up the temp file on failure
    /// </summary>
    /// <param name="path">The destination file path</param>
    /// <param name="contents">The text to write</param>
    /// <returns>A task that completes when the file has been durably replaced</returns>
    public static async Task WriteAllTextAsync(string path, string contents)
    {
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                       4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(contents);
                await writer.FlushAsync();
                // The BCL has no async fsync; the final flush-to-disk is synchronous.
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, path, overwrite: true);
            SyncParentDirectory(path);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Writes text to a file atomically via temp file, fsync and rename; cleans up the temp file on failure
    /// </summary>
    /// <param name="path">The destination file path</param>
    /// <param name="contents">The text to write</param>
    public static void WriteAllText(string path, string contents)
    {
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, path, overwrite: true);
            SyncParentDirectory(path);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Deletes orphaned temp files left by a crash between write and rename.
    /// Call at store startup; only *.tmp files in the data directory are touched.
    /// </summary>
    public static void CleanupTempFiles(string directory)
    {
        if (!Directory.Exists(directory)) return;
        try
        {
            foreach (var temp in Directory.EnumerateFiles(directory, "*.tmp", SearchOption.AllDirectories))
            {
                TryDelete(temp);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void SyncParentDirectory(string path)
    {
        // On Windows the rename is durable once the file is fsynced; on Linux the
        // directory entry must be fsynced too. Opening a directory as a read-only
        // stream works on Linux and is skipped elsewhere.
        if (!OperatingSystem.IsLinux()) return;
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (dir == null) return;
            using var handle = new FileStream(dir, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            handle.Flush(flushToDisk: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}