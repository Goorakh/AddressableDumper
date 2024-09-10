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
using UnityEngine.ResourceManagement.ResourceProviders;

namespace AddressableDumper
{
    public static class AddressablesIterator
    {
        const int ASSET_COUNT_ESTIMATE = 20000;

        static readonly string _addressablesCachePath = System.IO.Path.Combine(Main.PersistentSaveDataDirectory, "cache");

        static readonly string _addressablesCacheVersionPath = System.IO.Path.Combine(_addressablesCachePath, "version");

        static readonly string _addressableKeysCachePath = System.IO.Path.Combine(_addressablesCachePath, "keys");

        static readonly string _addressableSceneKeysCachePath = System.IO.Path.Combine(_addressablesCachePath, "scenes");

        static readonly string _addressablesKeysDumpPath = System.IO.Path.Combine(Main.PersistentSaveDataDirectory, "keys_dump.txt");

        static AssetInfo[] _allAssetInfos = [];

        static IReadOnlyDictionary<UnityEngine.Object, AssetInfo> _assetInfoLookup;
        public static IReadOnlyDictionary<UnityEngine.Object, AssetInfo> AssetInfoLookup => _assetInfoLookup ??= new AssetLookup(GetAllAssets());

        static IResourceLocation[] _sceneLocations = [];

        static bool isValidAsset(Type assetType)
        {
            return typeof(UnityEngine.Object).IsAssignableFrom(assetType);
        }

        public static AssetInfo[] GetAllAssets()
        {
            return _allAssetInfos;
        }

        public static IResourceLocation[] GetSceneResourceLocations()
        {
            return _sceneLocations;
        }

        [SystemInitializer]
        static void Init()
        {
            Directory.CreateDirectory(_addressablesCachePath);

            string currentVersion = Application.version;

            bool refresh;
            if (File.Exists(_addressablesCacheVersionPath))
            {
                string cacheVersion = File.ReadAllText(_addressablesCacheVersionPath).Trim();
                refresh = cacheVersion != currentVersion;
            }
            else
            {
                refresh = true;
            }

            if (!File.Exists(_addressableKeysCachePath))
            {
                refresh = true;
            }

            if (!File.Exists(_addressableSceneKeysCachePath))
            {
                refresh = true;
            }

            if (!File.Exists(_addressablesKeysDumpPath))
            {
                refresh = true;
            }

            if (refresh)
            {
                Log.Warning("Cache refresh required");
            }
            else
            {
                List<AssetInfo> loadedAssetInfos = new List<AssetInfo>(ASSET_COUNT_ESTIMATE);

                using (FileStream keysCacheFile = File.Open(_addressableKeysCachePath, FileMode.Open, FileAccess.Read))
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

                List<SceneInstance> sceneInstances = new List<SceneInstance>();

                using (FileStream scenesCacheFile = File.Open(_addressableSceneKeysCachePath, FileMode.Open, FileAccess.Read))
                {
                    using (BinaryReader reader = new BinaryReader(scenesCacheFile, Encoding.UTF8, true))
                    {
                        HashSet<IResourceLocation> resourceLocations = [];

                        while (scenesCacheFile.Position < scenesCacheFile.Length)
                        {
                            string key = reader.ReadString();

                            foreach (IResourceLocator resourceLocator in Addressables.ResourceLocators)
                            {
                                if (resourceLocator.Locate(key, typeof(SceneInstance), out IList<IResourceLocation> locations))
                                {
                                    foreach (IResourceLocation location in locations)
                                    {
                                        resourceLocations.Add(location);
                                    }
                                }
                            }
                        }

                        _sceneLocations = resourceLocations.ToArray();
                    }
                }

                Log.Info($"Loaded {loadedAssetInfos.Count} locations from cache");
                _allAssetInfos = loadedAssetInfos.ToArray();

                Log.Info($"Loaded {_sceneLocations.Length} scenes from cache");
            }
        }

        [ConCommand(commandName = "refresh_addressables_key_cache")]
        static void CCRefreshCache(ConCommandArgs args)
        {
            refreshCache();
        }

        static void refreshCache()
        {
            Log.Info("Refreshing keys cache...");

            List<AssetInfo> assetInfos = new List<AssetInfo>(ASSET_COUNT_ESTIMATE);
            List<IResourceLocation> sceneLocations = [];

            HashSet<IResourceLocation> resourceLocations = [];

            foreach (IResourceLocator locator in Addressables.ResourceLocators)
            {
                Log.Info($"Collecting keys from resource locator: {locator.LocatorId}");

                if (locator is ResourceLocationMap resourceLocationMap)
                {
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

                            if (resourceLocations.Add(location))
                            {
                                if (typeof(UnityEngine.Object).IsAssignableFrom(location.ResourceType))
                                {
                                    assetInfos.Add(new AssetInfo(location));
                                }
                                else if (location.ResourceType == typeof(SceneInstance))
                                {
                                    sceneLocations.Add(location);
                                }
                                else
                                {
#if DEBUG
                                    Log.Debug($"Skipping invalid asset type {location.ResourceType.Name} ({location.PrimaryKey})");
#endif
                                }
                            }
                        }
                    }
                }
            }

            assetInfos.Sort();

            _allAssetInfos = assetInfos.ToArray();

            _assetInfoLookup = null;

            _sceneLocations = sceneLocations.ToArray();
            Array.Sort(_sceneLocations, (a, b) => a.PrimaryKey.CompareTo(b.PrimaryKey));

            Log.Info($"Found {_allAssetInfos.Length} locations");

            using (FileStream keysCacheFile = File.Open(_addressableKeysCachePath, FileMode.Create, FileAccess.Write))
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

            Log.Info($"Found {sceneLocations.Count} scenes");

            using (FileStream scenesCacheFile = File.Open(_addressableSceneKeysCachePath, FileMode.Create, FileAccess.Write))
            {
                using (BinaryWriter writer = new BinaryWriter(scenesCacheFile, Encoding.UTF8, true))
                {
                    foreach (IResourceLocation location in sceneLocations)
                    {
                        writer.Write(location.PrimaryKey);
                    }
                }
            }

            File.WriteAllLines(_addressablesKeysDumpPath, _allAssetInfos.Select(a => $"{a.Key}\t\t({a.AssetType?.FullName ?? "null"})"));
            File.WriteAllText(_addressablesCacheVersionPath, Application.version);
        }
    }
}
