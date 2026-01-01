using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LaunchPlugin
{
    /// <summary>
    /// File helper patterned after launch trace management. No UI dependencies.
    /// </summary>
    public sealed class SessionFileManager
    {
        public string SanitizeName(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return "Unknown";
            }

            var invalidChars = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars());
            string result = candidate;
            foreach (var c in invalidChars)
            {
                result = result.Replace(c, '_');
            }

            return result;
        }

        public IReadOnlyList<string> ListFiles(string directory, string searchPattern)
        {
            if (!Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(directory, searchPattern)
                .OrderByDescending(f => File.GetCreationTime(f))
                .ToList();
        }

        public bool TryDeleteFile(string filePath, out Exception failure)
        {
            failure = null;
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                return true;
            }
            catch (Exception ex)
            {
                failure = ex;
                return false;
            }
        }
    }
}
