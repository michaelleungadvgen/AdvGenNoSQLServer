// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Text.Json;

namespace AdvGenNoSqlServer.Core.Authentication;

/// <summary>
/// Stores users in a JSON file, independent of the document store so it keeps
/// working under CacheOnly mode. Writes are atomic (temp file + move). A corrupt
/// file is backed up as users.json.corrupt-&lt;timestamp&gt; and treated as empty.
/// </summary>
public sealed class FileUserStore : IUserStore
{
    private sealed class FileShape { public List<PersistedUser> Users { get; set; } = new(); }

    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// Initializes a new instance of the <see cref="FileUserStore"/> class backed by the given JSON file path
    /// </summary>
    /// <param name="path">Path of the users JSON file</param>
    public FileUserStore(string path) => _path = path;

    /// <summary>
    /// Loads persisted users from the JSON file; returns an empty list when the file is missing, backing up a corrupt file
    /// </summary>
    /// <returns>The persisted user records</returns>
    public IReadOnlyList<PersistedUser> Load()
    {
        if (!File.Exists(_path)) return Array.Empty<PersistedUser>();
        try
        {
            var json = File.ReadAllText(_path);
            var shape = JsonSerializer.Deserialize<FileShape>(json, Options);
            return shape?.Users ?? new List<PersistedUser>();
        }
        catch (JsonException)
        {
            var backup = _path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            try { File.Copy(_path, backup, overwrite: true); } catch (IOException) { }
            return Array.Empty<PersistedUser>();
        }
    }

    /// <summary>
    /// Atomically saves the users to the JSON file (temp file + move) and restricts file permissions on Unix
    /// </summary>
    /// <param name="users">The user records to persist</param>
    public void Save(IEnumerable<PersistedUser> users)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var shape = new FileShape { Users = users.ToList() };
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(shape, Options));
        File.Move(tmp, _path, overwrite: true);

        // The file contains password hashes: owner-only read/write on Unix.
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch (IOException) { /* best effort — filesystem may not support modes */ }
        }
    }
}
