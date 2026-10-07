using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EdenApis
{
    [InitializeOnLoad]
    internal static class EdenBadgeSettings
    {
        internal const string BadgeVersion = "2.0.0";
        internal const int SettingsVersion = 1;
        internal const string AssetPath = "Assets/EdenApis/EdenBadge/Settings.json";

        private const string LegacySessionFileName = "AtlasGeneratorSession.json";
        private const string LegacySessionPath =
            "Assets/EdenBadgeV2/AtlasGenerator/Config/AtlasGeneratorSession.json";

        private static bool resolved;
        private static bool hadSavedSettings;
        private static bool legacyRetired;
        private static bool warnedNewerSettings;
        private static bool restoreFinished;

        static EdenBadgeSettings()
        {
            EditorApplication.delayCall += OnEditorLoad;
        }

        private static void OnEditorLoad()
        {
            Data data = Load();
            if (RestoreAtlasIfNeeded(data))
                Write(data);
        }

        internal static bool HadSavedSettings
        {
            get { return hadSavedSettings; }
        }

        internal static Data Load()
        {
            string legacyPath = FindLegacyAssetPath();
            if (!resolved)
            {
                hadSavedSettings = File.Exists(ToAbsolute(AssetPath)) || legacyPath != null;
                resolved = true;
            }

            Data current = Read(AssetPath);
            Data legacy = legacyPath == null ? null : Read(legacyPath);
            Data data;
            bool absorbLegacy = false;

            if (current == null && legacy != null)
            {
                data = legacy;
                absorbLegacy = true;
            }
            else if (current != null && legacy != null && IsUnused(current) && !IsUnused(legacy))
            {
                current.custom1 = legacy.custom1;
                current.custom2 = legacy.custom2;
                data = current;
                absorbLegacy = true;
            }
            else
            {
                data = current ?? new Data();
            }

            if (data.custom1 == null)
                data.custom1 = new Slot();
            if (data.custom2 == null)
                data.custom2 = new Slot();

            bool upgraded = Upgrade(data);
            if (data.settingsVersion <= SettingsVersion && (upgraded || absorbLegacy || current == null))
                Write(data);

            if (legacyPath != null
                && legacyPath.StartsWith("Assets/", StringComparison.Ordinal)
                && File.Exists(ToAbsolute(AssetPath)))
            {
                AssetDatabase.DeleteAsset(legacyPath);
                if (absorbLegacy)
                    Debug.Log("Eden Badge moved Atlas Generator settings to " + AssetPath + ".");
            }

            legacyRetired = true;
            return data;
        }

        internal static void Save(Slot custom1, Slot custom2)
        {
            Data data = Load();
            if (data.settingsVersion > SettingsVersion)
                return;

            data.custom1 = custom1 ?? new Slot();
            data.custom2 = custom2 ?? new Slot();
            Write(data);
        }

        private static bool Upgrade(Data data)
        {
            if (data.settingsVersion > SettingsVersion)
            {
                if (!warnedNewerSettings)
                {
                    warnedNewerSettings = true;
                    Debug.LogWarning(
                        "Eden Badge settings were written by a newer version. They were left unchanged.");
                }

                return false;
            }

            bool changed = false;
            if (data.settingsVersion < 1)
            {
                data.settingsVersion = 1;
                changed = true;
            }

            string previous = data.badgeVersion ?? "";
            if (previous != BadgeVersion)
            {
                if (previous.Length > 0)
                    Debug.Log("Eden Badge settings are being brought forward from version " + previous + ".");

                data.badgeVersion = BadgeVersion;
                changed = true;
            }

            return changed;
        }

        internal static void MarkAtlasBaked()
        {
            Data data = Read(AssetPath);
            if (data == null || data.atlasBakedVersion == BadgeVersion)
                return;

            data.atlasBakedVersion = BadgeVersion;
            Write(data);
            restoreFinished = true;
        }

        private static bool RestoreAtlasIfNeeded(Data data)
        {
            if (restoreFinished || data.atlasBakedVersion == BadgeVersion)
                return false;

            if (!HasRestorableWork(data.custom1) && !HasRestorableWork(data.custom2))
            {
                data.atlasBakedVersion = BadgeVersion;
                restoreFinished = true;
                return true;
            }

            restoreFinished = true;
            if (!AtlasGenerator.AtlasGeneratorWindow.TryBakeFromSettings(data))
                return false;

            data.atlasBakedVersion = BadgeVersion;
            Debug.Log("Eden Consent Badge rebuilt the custom pictures from the saved settings.");
            return true;
        }

        private static bool HasRestorableWork(Slot slot)
        {
            return slot != null && (slot.cleared || !string.IsNullOrEmpty(slot.textureGuid));
        }

        private static void Write(Data data)
        {
            string absolute = ToAbsolute(AssetPath);
            string folder = Path.GetDirectoryName(absolute);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            bool creating = !File.Exists(absolute);
            File.WriteAllText(absolute, JsonUtility.ToJson(data, true));
            if (creating)
                AssetDatabase.ImportAsset(AssetPath);
        }

        private static Data Read(string assetPath)
        {
            string absolute = ToAbsolute(assetPath);
            if (!File.Exists(absolute))
                return null;

            try
            {
                return JsonUtility.FromJson<Data>(File.ReadAllText(absolute));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Eden Badge could not read " + assetPath + ". " + exception.Message);
                return null;
            }
        }

        private static string FindLegacyAssetPath()
        {
            if (legacyRetired)
                return null;

            if (File.Exists(ToAbsolute(LegacySessionPath)))
                return LegacySessionPath;

            string[] guids = AssetDatabase.FindAssets("t:AtlasGeneratorConfig");
            if (guids.Length == 0)
                return null;

            string configPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            if (string.IsNullOrEmpty(configPath))
                return null;

            string folder = Path.GetDirectoryName(configPath);
            if (string.IsNullOrEmpty(folder))
                return null;

            string candidate = Path.Combine(folder, LegacySessionFileName).Replace('\\', '/');
            if (candidate == AssetPath || !File.Exists(ToAbsolute(candidate)))
                return null;

            return candidate;
        }

        private static bool IsUnused(Data data)
        {
            return IsUnused(data.custom1) && IsUnused(data.custom2);
        }

        private static bool IsUnused(Slot slot)
        {
            return slot == null || (!slot.cleared && string.IsNullOrEmpty(slot.textureGuid));
        }

        private static string ToAbsolute(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath);
        }

        [Serializable]
        internal class Data
        {
            public int settingsVersion;
            public string badgeVersion = "";
            public string atlasBakedVersion = "";
            public Slot custom1 = new Slot();
            public Slot custom2 = new Slot();
        }

        [Serializable]
        internal class Slot
        {
            public string textureGuid = "";
            public float scale = 1f;
            public float tolerance = 0.5f;
            public int maskMode;
            public bool cutoutBackground;
            public float backgroundR;
            public float backgroundG;
            public float backgroundB;
            public float backgroundA = 1f;
            public bool cleared;
        }
    }
}
