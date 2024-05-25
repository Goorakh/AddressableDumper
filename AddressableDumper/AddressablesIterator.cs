using RoR2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace AddressableDumper
{
    public static class AddressablesIterator
    {
        static string[] _assetKeys = [];

        static bool isID(string key)
        {
            foreach (char c in key)
            {
                if (!char.IsDigit(c) && !(char.IsLower(c) && c >= 'a' && c <= 'f'))
                {
                    return false;
                }
            }

            return true;
        }

        static bool isValidAsset(string key)
        {
            return Addressables.LoadAssetAsync<object>(key).WaitForCompletion() is UnityEngine.Object unityObject && unityObject;
        }

        static bool isValidAsset(AssetInfo assetInfo)
        {
            return assetInfo.Asset is UnityEngine.Object unityObject && unityObject;
        }

        public static IEnumerable<AssetInfo> LoadAllAssets()
        {
            for (int i = 0; i < _assetKeys.Length; i++)
            {
                string key = _assetKeys[i];

                Log.Info($"Loading asset {i + 1}/{_assetKeys.Length}: {key}");

                yield return new AssetInfo(key);
            }
        }

        [SystemInitializer]
        static void Init()
        {
            string addressablesCachePath = System.IO.Path.Combine(Main.PersistentSaveDataDirectory, "cache");
            Directory.CreateDirectory(addressablesCachePath);

            string currentVersion = Application.version;
            string addressablesCacheVersionPath = System.IO.Path.Combine(addressablesCachePath, "version");

            bool refresh;
            if (File.Exists(addressablesCacheVersionPath))
            {
                string cacheVersion = File.ReadAllText(addressablesCacheVersionPath).Trim();
                refresh = cacheVersion != currentVersion;
            }
            else
            {
                refresh = true;
            }

            string addressableKeysCachePath = System.IO.Path.Combine(addressablesCachePath, "keys");
            if (!File.Exists(addressableKeysCachePath))
            {
                refresh = true;
            }

            string addressablesKeysDumpPath = System.IO.Path.Combine(Main.PersistentSaveDataDirectory, "keys_dump.txt");
            if (!File.Exists(addressablesKeysDumpPath))
            {
                refresh = true;
            }

            if (refresh)
            {
                Log.Info("Refreshing keys cache...");

                AssetInfo[] assets = Addressables.ResourceLocators.SelectMany(locator => locator.Keys).Select(key => key?.ToString()).Where(key =>
                {
                    if (string.IsNullOrWhiteSpace(key))
                        return false;

                    if (int.TryParse(key, out _))
                    {
                        return false;
                    }

                    if (isID(key))
                    {
                        return false;
                    }

                    // These assets cause the game to freeze indefinitely when trying to load them, so just manually exclude them all
                    switch (key)
                    {
                        case "Advanced_Pressed_mini":
                        case "Advanced_UnPressed_mini":
                        case "Button_Off":
                        case "Button_On":
                        case "DebugUI Canvas":
                        case "DebugUI Persistent Canvas":
                        case "Icon":
                        case "Materials/Collider":
                        case "Materials/EdgePicker":
                        case "Materials/EdgePickerHDRP":
                        case "Materials/FacePicker":
                        case "Materials/FacePickerHDRP":
                        case "Materials/InvisibleFace":
                        case "Materials/NoDraw":
                        case "Materials/ProBuilderDefault":
                        case "Materials/StandardVertexColorHDRP":
                        case "Materials/StandardVertexColorLWRP":
                        case "Materials/Trigger":
                        case "Materials/UnlitVertexColor":
                        case "Materials/VertexPicker":
                        case "Materials/VertexPickerHDRP":
                        case "Missing Object":
                        case "Textures/GridBox_Default":
#if DEBUG
                            Log.Debug($"Skipping key {key}: blacklist");
#endif
                            return false;
                    }

                    return true;
                }).Select(key => new AssetInfo(key)).Where(asset =>
                {
                    if (!isValidAsset(asset))
                    {
#if DEBUG
                        Log.Debug($"Skipping {asset}: invalid asset");
#endif
                        return false;
                    }

#if DEBUG
                    Log.Debug($"Found valid key: {asset.Key}");
#endif

                    return true;
                }).OrderBy(s => s.Key).ToArray();

                _assetKeys = Array.ConvertAll(assets, a => a.Key);

                File.WriteAllLines(addressableKeysCachePath, _assetKeys);
                File.WriteAllLines(addressablesKeysDumpPath, assets.Select(a => $"{a.Key}\t\t({a.AssetType?.FullName ?? "null"})"));
                File.WriteAllText(addressablesCacheVersionPath, currentVersion);
            }
            else
            {
                _assetKeys = File.ReadAllLines(addressableKeysCachePath);
            }
        }
    }
}
