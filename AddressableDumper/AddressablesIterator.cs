using AddressableDumper.Utils;
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

using Path = System.IO.Path;

namespace AddressableDumper
{
    public static class AddressablesIterator
    {
        static readonly string _addressablesCachePath = Path.Combine(Main.PersistentSaveDataDirectory, "cache");

        static readonly string _addressablesCacheVersionPath = Path.Combine(_addressablesCachePath, "version");

        static readonly string _cachedModVersionPath = Path.Combine(_addressablesCachePath, "mod_version");

        static readonly string _addressableKeysCachePath = Path.Combine(_addressablesCachePath, "keys");

        static readonly string _addressableSceneKeysCachePath = Path.Combine(_addressablesCachePath, "scenes");

        static readonly string _addressablesKeysDumpPath = Path.Combine(Main.PersistentSaveDataDirectory, "keys_dump.txt");

        static AssetInfo[] _allAssetInfos = [];

        static IReadOnlyDictionary<UnityEngine.Object, AssetInfo> _assetInfoLookup;
        public static IReadOnlyDictionary<UnityEngine.Object, AssetInfo> AssetInfoLookup => _assetInfoLookup ??= new AssetLookup(GetAllAssetsFlattened());

        static IResourceLocation[] _sceneLocations = [];

        static bool isValidAsset(Type assetType)
        {
            return typeof(UnityEngine.Object).IsAssignableFrom(assetType);
        }

        public static AssetInfo[] GetAllAssets()
        {
            return _allAssetInfos;
        }

        public static IEnumerable<AssetInfo> GetAllAssetsFlattened()
        {
            return _allAssetInfos.SelectMany<AssetInfo, AssetInfo>(assetInfo => [assetInfo, .. assetInfo.SubAssets]);
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
            const string CurrentModVersion = Main.PluginVersion;

            bool refresh = false;
            if (File.Exists(_addressablesCacheVersionPath))
            {
                string cacheVersion = File.ReadAllText(_addressablesCacheVersionPath).Trim();
                refresh |= cacheVersion != currentVersion;
            }
            else
            {
                refresh = true;
            }

            if (File.Exists(_cachedModVersionPath))
            {
                string cacheModVersion = File.ReadAllText(_cachedModVersionPath).Trim();
                refresh |= cacheModVersion != CurrentModVersion;
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
                List<AssetInfo> loadedAssetInfos = [];

                using (FileStream keysCacheFile = File.Open(_addressableKeysCachePath, FileMode.Open, FileAccess.Read))
                {
                    using (BinaryReader reader = new BinaryReader(keysCacheFile, Encoding.UTF8, true))
                    {
                        HashSet<IResourceLocation> resourceLocations = [];

                        while (keysCacheFile.Position < keysCacheFile.Length)
                        {
                            bool tryDeserializeAssetInfo(out AssetInfo assetInfo)
                            {
                                string key = reader.ReadString();
                                string typeName = reader.ReadString();
                                string objectName = reader.ReadString();

                                int subAssetCount = reader.ReadInt32();
                                List<AssetInfo> subAssets = new List<AssetInfo>(subAssetCount);
                                for (int i = 0; i < subAssetCount; i++)
                                {
                                    if (tryDeserializeAssetInfo(out AssetInfo subAssetInfo))
                                    {
                                        subAssets.Add(subAssetInfo);
                                    }
                                }

                                Type assetType = Type.GetType(typeName, false);
                                if (assetType == null)
                                {
                                    Log.Error($"Could not resolve type {typeName}");
                                    assetInfo = default;
                                    return false;
                                }

                                foreach (IResourceLocator resourceLocator in Addressables.ResourceLocators)
                                {
                                    if (resourceLocator.Locate(key, assetType, out IList<IResourceLocation> locations))
                                    {
                                        foreach (IResourceLocation location in locations)
                                        {
                                            if (resourceLocations.Add(location))
                                            {
                                                assetInfo = new AssetInfo(location, objectName, [.. subAssets]);
                                                return true;
                                            }
                                        }
                                    }
                                }

                                Log.Error($"Failed to locate cached asset: {key} ({typeName})");

                                assetInfo = default;
                                return false;
                            }

                            if (tryDeserializeAssetInfo(out AssetInfo assetInfo))
                            {
                                loadedAssetInfos.Add(assetInfo);
                            }
                        }
                    }
                }

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

            List<AssetInfo> assetInfos = [];
            List<IResourceLocation> sceneLocations = [];

            HashSet<IResourceLocation> handledResourceLocations = new HashSet<IResourceLocation>(ResourceLocationComparer.Instance);

            foreach (IResourceLocator locator in Addressables.ResourceLocators)
            {
                Log.Info($"Collecting keys from resource locator: {locator.LocatorId}");
                
                if (locator is not ResourceLocationMap resourceLocationMap)
                    continue;

                foreach ((object key, IList<IResourceLocation> resourceLocations) in resourceLocationMap.Locations)
                {
                    if (resourceLocations == null || resourceLocations.Count == 0)
                        continue;

                    // Only load the primary location to avoid duplicates, sub-assets will be loaded later
                    IResourceLocation resourceLocation = resourceLocations[0];
                    //foreach (IResourceLocation resourceLocation in resourceLocations)
                    {
                        if (resourceLocation.ProviderId == "UnityEngine.ResourceManagement.ResourceProviders.LegacyResourcesProvider")
                        {
                            Log.Info($"Skipping invalid asset provider {resourceLocation.ProviderId} ({key})");
                            continue;
                        }

                        if (handledResourceLocations.Add(resourceLocation))
                        {
                            if (typeof(UnityEngine.Object).IsAssignableFrom(resourceLocation.ResourceType))
                            {
                                AssetInfo[] subAssetInfos = [];
                                if (resourceLocation.HasDependencies)
                                {
                                    IResourceLocation assetBundleLocation = resourceLocation.Dependencies.FirstOrDefault();
                                    if (assetBundleLocation != null && assetBundleLocation.ResourceType == typeof(IAssetBundleResource))
                                    {
                                        AssetBundle sourceAssetBundle = Addressables.LoadAssetAsync<IAssetBundleResource>(assetBundleLocation).WaitForCompletion().GetAssetBundle();

                                        if (sourceAssetBundle)
                                        {
                                            UnityEngine.Object[] subAssets = sourceAssetBundle.LoadAssetWithSubAssets(resourceLocation.InternalId);
                                            // index 0 is the main asset
                                            if (subAssets.Length > 1)
                                            {
                                                subAssetInfos = new AssetInfo[subAssets.Length - 1];
                                                for (int i = 0; i < subAssetInfos.Length; i++)
                                                {
                                                    UnityEngine.Object subAsset = subAssets[i + 1];

                                                    string subAssetName = subAsset.name;

                                                    ResourceLocationBase subAssetLocation = new ResourceLocationBase(resourceLocation.PrimaryKey + "[" + subAssetName + "]", resourceLocation.InternalId + "[" + subAssetName + "]", resourceLocation.ProviderId, subAsset.GetType(), [.. resourceLocation.Dependencies]);

                                                    if (!handledResourceLocations.Add(subAssetLocation))
                                                    {
                                                        Log.Error($"Sub-asset {subAssetLocation.PrimaryKey}, {subAssetLocation.InternalId} ({subAssetLocation.ResourceType}) has already been added");
                                                    }

                                                    subAssetInfos[i] = new AssetInfo(subAssetLocation, subAssetName, []);
                                                }

                                                Log.Debug($"Found {subAssetInfos.Length} sub asset(s) for {resourceLocation.PrimaryKey}");
                                            }
                                        }
                                    }
                                }

                                assetInfos.Add(new AssetInfo(resourceLocation, subAssetInfos));
                            }
                            else if (resourceLocation.ResourceType == typeof(SceneInstance))
                            {
                                sceneLocations.Add(resourceLocation);
                            }
                            else
                            {
                                Log.Info($"Skipping invalid asset type {resourceLocation.ResourceType.Name} ({key})");
                            }
                        }
                    }
                }
            }

            assetInfos.Sort();

            _allAssetInfos = [.. assetInfos];

            _assetInfoLookup = null;

            Log.Info($"Found {_allAssetInfos.Length} locations");

            using (FileStream keysCacheFile = File.Open(_addressableKeysCachePath, FileMode.Create, FileAccess.Write))
            {
                using (BinaryWriter writer = new BinaryWriter(keysCacheFile, Encoding.UTF8, true))
                {
                    void serializeAssetInfo(in AssetInfo assetInfo)
                    {
                        writer.Write(assetInfo.Key);
                        writer.Write(assetInfo.AssetType.AssemblyQualifiedName);
                        writer.Write(assetInfo.ObjectName ?? string.Empty);

                        writer.Write(assetInfo.SubAssets.Length);
                        foreach (AssetInfo subAssetInfo in assetInfo.SubAssets)
                        {
                            serializeAssetInfo(subAssetInfo);
                        }
                    }

                    foreach (AssetInfo assetInfo in _allAssetInfos)
                    {
                        serializeAssetInfo(assetInfo);
                    }
                }
            }

            _sceneLocations = [.. sceneLocations];
            Array.Sort(_sceneLocations, (a, b) => string.Compare(a.PrimaryKey, b.PrimaryKey));

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
            File.WriteAllText(_cachedModVersionPath, Main.PluginVersion);
        }
    }
}
