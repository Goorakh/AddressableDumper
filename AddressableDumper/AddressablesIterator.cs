using AddressableDumper.ValueDumper;
using RoR2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace AddressableDumper
{
    public static class AddressablesIterator
    {
        static AssetInfo[] _allAssetInfos = [];

        static IReadOnlyDictionary<UnityEngine.Object, AssetInfo> _assetInfoLookup;
        public static IReadOnlyDictionary<UnityEngine.Object, AssetInfo> AssetInfoLookup => _assetInfoLookup ??= new AssetLookup(GetAllAssets());

        static bool isValidAsset(Type assetType)
        {
            return typeof(UnityEngine.Object).IsAssignableFrom(assetType);
        }

        public static AssetInfo[] GetAllAssets()
        {
            return _allAssetInfos;
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

                _allAssetInfos = Addressables.ResourceLocators.SelectMany(locator =>
                {
                    Log.Info($"Collecting keys from resource locator: {locator.LocatorId}");

                    if (locator is ResourceLocationMap resourceLocationMap)
                    {
                        HashSet<IResourceLocation> resourceLocations = [];

                        foreach (IList<IResourceLocation> locations in resourceLocationMap.Locations.Values)
                        {
                            foreach (IResourceLocation location in locations)
                            {
                                if (location.ProviderId == "UnityEngine.ResourceManagement.ResourceProviders.LegacyResourcesProvider")
                                {
#if DEBUG
                                    Log.Debug($"Skipping invalid asset provider {location.ProviderId} ({location.PrimaryKey})");
#endif
                                    continue;
                                }

                                if (!isValidAsset(location.ResourceType))
                                {
#if DEBUG
                                    Log.Debug($"Skipping invalid asset type {location.ResourceType.Name} ({location.PrimaryKey})");
#endif
                                    continue;
                                }

                                resourceLocations.Add(location);
                            }
                        }

                        return resourceLocations;
                    }

                    return [];
                }).Select(l => new AssetInfo(l)).OrderBy(a => a.Key).ToArray();

                Log.Info($"Found {_allAssetInfos.Length} locations");

                using (FileStream keysCacheFile = File.Open(addressableKeysCachePath, FileMode.Create, FileAccess.Write))
                {
                    using (BinaryWriter writer = new BinaryWriter(keysCacheFile, Encoding.UTF8, true))
                    {
                        foreach (AssetInfo assetInfo in _allAssetInfos)
                        {
                            writer.Write(assetInfo.Key);
                            writer.Write(assetInfo.AssetType.AssemblyQualifiedName);
                            writer.Write(assetInfo.ObjectName ?? string.Empty);
                        }
                    }
                }

                File.WriteAllLines(addressablesKeysDumpPath, _allAssetInfos.Select(a => $"{a.Key}\t\t({a.AssetType?.FullName ?? "null"})"));
                File.WriteAllText(addressablesCacheVersionPath, currentVersion);
            }
            else
            {
                List<AssetInfo> loadedAssetInfos = [];

                using (FileStream keysCacheFile = File.Open(addressableKeysCachePath, FileMode.Open, FileAccess.Read))
                {
                    using (BinaryReader reader = new BinaryReader(keysCacheFile, Encoding.UTF8, true))
                    {
                        HashSet<IResourceLocation> resourceLocations = [];

                        while (keysCacheFile.Position < keysCacheFile.Length)
                        {
                            string key = reader.ReadString();
                            string typeName = reader.ReadString();
                            string objectName = reader.ReadString();

                            Type assetType = Type.GetType(typeName, false);
                            if (assetType == null)
                            {
                                Log.Error($"Could not resolve type {typeName}");
                                continue;
                            }

                            foreach (IResourceLocator resourceLocator in Addressables.ResourceLocators)
                            {
                                if (resourceLocator.Locate(key, assetType, out IList<IResourceLocation> locations))
                                {
                                    foreach (IResourceLocation location in locations)
                                    {
                                        if (resourceLocations.Add(location))
                                        {
                                            loadedAssetInfos.Add(new AssetInfo(location, objectName));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                Log.Info($"Loaded {loadedAssetInfos.Count} locations from cache");
                _allAssetInfos = loadedAssetInfos.ToArray();
            }
        }
    }
}
