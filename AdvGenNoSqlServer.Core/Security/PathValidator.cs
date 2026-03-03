// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.IO;
using System.Linq;

namespace AdvGenNoSqlServer.Core.Security;

/// <summary>
/// Provides utility methods for validating file paths to prevent path traversal attacks.
/// </summary>
public static class PathValidator
{
    /// <summary>
    /// Validates that a combined path is within the specified base directory.
    /// </summary>
    /// <param name="basePath">The expected base directory.</param>
    /// <param name="pathToCheck">The path to validate.</param>
    /// <returns>The full path if valid; otherwise, throws a <see cref="UnauthorizedAccessException"/>.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when the path is outside the base directory.</exception>
    public static string GetSafePath(string basePath, string pathToCheck)
    {
        var fullBasePath = Path.GetFullPath(basePath);
        var fullPathToCheck = Path.GetFullPath(pathToCheck);

        // Ensure the base path ends with a directory separator to prevent prefix-based traversal
        // e.g., /home/app/data matching /home/app/data_secrets
        var separator = Path.DirectorySeparatorChar.ToString();
        var fullBasePathWithSeparator = fullBasePath.EndsWith(separator)
            ? fullBasePath
            : fullBasePath + separator;

        if (!fullPathToCheck.StartsWith(fullBasePathWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !fullPathToCheck.Equals(fullBasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Path traversal attempt detected: {pathToCheck} is outside of {basePath}");
        }

        return fullPathToCheck;
    }
}
