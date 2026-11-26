using AddressableDumper.Utils;
using AddressableDumper.ValueDumper;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace AddressableDumper
{
    public static class AddressablesIterator
    {
        static AssetInfo[] _allAssetInfos = [];

        static AssetLookup _assetInfoLookup;
        public static AssetLookup AssetInfoLookup => _assetInfoLookup ??= new AssetLookup(GetAllAssetsFlattened());

        static IResourceLocation[] _sceneLocations = [];

        public static bool HasLoadedAssets => _allAssetInfos != null && _allAssetInfos.Length > 0;

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

        [ConCommand(commandName = "refresh_addressables_key_cache")]
        static void CCRefreshCache(ConCommandArgs args)
        {
            RefreshCache();
        }

        public static void RefreshCache()
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

            _sceneLocations = [.. sceneLocations];
            Array.Sort(_sceneLocations, (a, b) => string.Compare(a.PrimaryKey, b.PrimaryKey));

            Log.Info($"Found {sceneLocations.Count} scenes");
        }
    }
}
