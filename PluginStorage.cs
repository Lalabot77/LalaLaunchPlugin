using SimHub.Plugins;
using System;
using System.IO;

namespace LaunchPlugin
{
    public static class PluginStorage
    {
        private static string _commonBase;

        public static void Initialize(PluginManager pluginManager)
        {
            if (!string.IsNullOrWhiteSpace(_commonBase))
                return;

            if (pluginManager == null) throw new ArgumentNullException(nameof(pluginManager));
            _commonBase = pluginManager.GetCommonStoragePath();
            EnsurePluginFolder();
        }

        public static string GetCommonFolder()
        {
            EnsurePluginFolder();
            var commonBase = ResolveCommonBase();
            Directory.CreateDirectory(commonBase);
            return commonBase;
        }

        public static string GetPluginFolder()
        {
            EnsurePluginFolder();
            var folder = Path.Combine(ResolveCommonBase(), "LalaPlugin");
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static string GetPluginFilePath(string fileName)
        {
            EnsurePluginFolder();
            return Path.Combine(GetPluginFolder(), fileName);
        }

        public static string GetCommonFilePath(string fileName)
        {
            EnsurePluginFolder();
            return Path.Combine(GetCommonFolder(), fileName);
        }

        public static bool TryMigrate(string legacyPath, string newPath)
        {
            if (string.IsNullOrWhiteSpace(legacyPath) || string.IsNullOrWhiteSpace(newPath))
                return false;

            if (File.Exists(newPath) || !File.Exists(legacyPath))
                return false;

            var folder = Path.GetDirectoryName(newPath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            File.Copy(legacyPath, newPath, overwrite: false);
            SimHub.Logging.Current.Info($"[LalaPlugin:Storage] migrated {legacyPath} -> {newPath}");
            return true;
        }

        private static void EnsurePluginFolder()
        {
            Directory.CreateDirectory(Path.Combine(ResolveCommonBase(), "LalaPlugin"));
        }

        private static string ResolveCommonBase()
        {
            if (!string.IsNullOrWhiteSpace(_commonBase))
                return _commonBase;

            var baseDir = AppDomain.CurrentDomain.BaseDirectory?.TrimEnd('\\', '/');
            return Path.Combine(baseDir ?? string.Empty, "PluginsData", "Common");
        }
    }
}
