// File: RacePresetStore.cs
// Purpose: Single-file JSON persistence for RacePreset[].
// Target: C# 7.3 / .NET Framework (Newtonsoft.Json preferred)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;

namespace LaunchPlugin
{
    public static class RacePresetStore
    {
        [JsonObject(MemberSerialization.OptIn)]
        private class RacePresetStoreRoot
        {
            [JsonProperty]
            public int SchemaVersion { get; set; } = 1;

            [JsonProperty]
            public List<RacePreset> Presets { get; set; } = new List<RacePreset>();
        }

        private const string NewFileName = "RacePresets.json";
        private const string LegacyFileName = "LalaLaunch.RacePresets.json";

        public static string GetFolderPath()
        {
            return PluginStorage.GetPluginFolder();
        }

        public static string GetFilePath() => Path.Combine(GetFolderPath(), NewFileName);

        // --- keep DefaultPresets(), SaveAll(), etc. unchanged ---

        public static List<RacePreset> LoadAll()
        {
            try
            {
                // 1) Ensure PluginsData\Common exists
                var folder = GetFolderPath();
                var path = GetFilePath();
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                // 2) One-time migration from legacy locations if present
                MigrateFromLegacyIfPresent(path);

                if (!File.Exists(path)) { var d = DefaultPresets(); SaveAll(d); return d; }

                var json = File.ReadAllText(path);
                List<RacePreset> list = null;
                try
                {
                    var store = JsonConvert.DeserializeObject<RacePresetStoreRoot>(json);
                    list = store?.Presets;
                }
                catch
                {
                    list = null;
                }

                if (list == null)
                {
                    list = JsonConvert.DeserializeObject<List<RacePreset>>(json);
                }

                if (list == null) list = new List<RacePreset>();

                if (list.Count == 0) { var d = DefaultPresets(); SaveAll(d); return d; }
                return list;
            }
            catch (Exception ex)
            {
                TryBackupCorrupt();
                var d = DefaultPresets(); SafeTry(() => SaveAll(d));
                DebugWrite("RacePresetStore: Error loading presets, wrote defaults. " + ex.Message);
                return d;
            }
        }

        // One-time migration helper
        private static void MigrateFromLegacyIfPresent(string destPath)
        {
            try
            {
                if (File.Exists(destPath))
                    return;

                var legacyCommonPath = PluginStorage.GetCommonFilePath(LegacyFileName);
                if (PluginStorage.TryMigrate(legacyCommonPath, destPath))
                    return;

                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var oldFolder = Path.Combine(docs, "SimHub", "LalaLaunch");
                var oldPath = Path.Combine(oldFolder, LegacyFileName);
                PluginStorage.TryMigrate(oldPath, destPath);
            }
            catch { /* best-effort */ }
        }

        /// <summary>
        /// Save all presets atomically (write to temp then replace).
        /// </summary>
        public static void SaveAll(List<RacePreset> presets)
        {
            if (presets == null) throw new ArgumentNullException(nameof(presets));

            var folder = GetFolderPath();
            var path = GetFilePath();
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var store = new RacePresetStoreRoot
            {
                Presets = presets
            };
            var json = JsonConvert.SerializeObject(store, Formatting.Indented);

            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(path))
                File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
            else
                File.Move(temp, path);

            DebugWrite($"RacePresetStore: Saved {presets.Count} preset(s).");
        }

        /// <summary>
        /// Simple default set to prove the plumbing. Adjust values later.
        /// </summary>
        public static List<RacePreset> DefaultPresets()
        {
            return new List<RacePreset>
            {
                new RacePreset
                {
                    Name = "IMSA 40m (1 stop window)",
                    Type = RacePresetType.TimeLimited,
                    RaceMinutes = 40,
                    MandatoryStopRequired = false,
                    TireChangeTimeSec = 0,        // 0 = no planned tyre change
                    MaxFuelLitres = null,         // null => leave current UI value
                    ContingencyInLaps = true,
                    ContingencyValue = 1.0
                },
                new RacePreset
                {
                    Name = "Sprint 25m (no stop)",
                    Type = RacePresetType.TimeLimited,
                    RaceMinutes = 25,
                    MandatoryStopRequired = false,
                    TireChangeTimeSec = 0,
                    MaxFuelLitres = null,
                    ContingencyInLaps = true,
                    ContingencyValue = 0.5
                },
                new RacePreset
                {
                    Name = "Fixed 30 Laps",
                    Type = RacePresetType.LapLimited,
                    RaceLaps = 30,
                    MandatoryStopRequired = false,
                    TireChangeTimeSec = 0,
                    MaxFuelLitres = null,
                    ContingencyInLaps = true,
                    ContingencyValue = 1.0
                }
            };
        }

        private static void TryBackupCorrupt()
        {
            try
            {
                var path = GetFilePath();
                if (File.Exists(path))
                {
                    var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var bak = Path.Combine(GetFolderPath(), $"RacePresets.corrupt.{stamp}.json");
                    File.Copy(path, bak, overwrite: false);
                    DebugWrite("RacePresetStore: Backed up possible corrupt file -> " + bak);
                }
            }
            catch { /* ignore backup issues */ }
        }

        private static void DebugWrite(string msg)
        {
            // No SimHub dependency here. If you have a logger, call it from outside.
            Debug.WriteLine(msg);
        }

        private static void SafeTry(Action a)
        {
            try { a(); } catch { /* swallow */ }
        }
    }
}
