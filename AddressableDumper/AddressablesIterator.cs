using RoR2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace AddressableDumper
{
    public static class AddressablesIterator
    {
        static IResourceLocation[] _assetLocations = [];

        static bool isValidAsset(Type assetType)
        {
            return typeof(UnityEngine.Object).IsAssignableFrom(assetType);
        }

        public static AssetInfo[] LoadAllAssets()
        {
            AssetInfo[] assetInfos = new AssetInfo[_assetLocations.Length];

            for (int i = 0; i < _assetLocations.Length; i++)
            {
                IResourceLocation location = _assetLocations[i];

                Log.Info($"Loading asset {i + 1}/{_assetLocations.Length}: {location.PrimaryKey}");

                assetInfos[i] = new AssetInfo(location);
            }

            return assetInfos;
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

                AssetInfo[] assets = Addressables.ResourceLocators.SelectMany(locator =>
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

                Log.Info($"Found {assets.Length} locations");

                _assetLocations = Array.ConvertAll(assets, a => a.Location);

                File.WriteAllLines(addressableKeysCachePath, _assetLocations.Select(l => l.PrimaryKey + "|" + l.ResourceType.AssemblyQualifiedName));

                File.WriteAllLines(addressablesKeysDumpPath, assets.Select(a => $"{a.Key}\t\t({a.AssetType?.FullName ?? "null"})"));
                File.WriteAllText(addressablesCacheVersionPath, currentVersion);
            }
            else
            {
                HashSet<IResourceLocation> resourceLocations = [];

                foreach (string serializedLocation in File.ReadAllLines(addressableKeysCachePath))
                {
                    string[] split = serializedLocation.Split('|');

                    string key = split[0];
                    string typeName = split[1];

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
                            resourceLocations.UnionWith(locations);
                        }
                    }
                }

                Log.Info($"Loaded {resourceLocations.Count} locations from cache");

                _assetLocations = resourceLocations.ToArray();
            }
        }
    }
}
