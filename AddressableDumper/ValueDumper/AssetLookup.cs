using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace AddressableDumper.ValueDumper
{
    public class AssetLookup : IReadOnlyDictionary<UnityEngine.Object, AssetInfo>
    {
        static readonly StringComparer _assetNameComparer = StringComparer.Ordinal;

        readonly AssetInfo[] _assets;

        readonly Dictionary<string, AssetNameGrouping> _objectNameGroupings;

        public AssetLookup(AssetInfo[] assetInfos)
        {
            List<AssetInfo> validAssetInfos = new List<AssetInfo>(assetInfos.Length);

            _objectNameGroupings = new Dictionary<string, AssetNameGrouping>(assetInfos.Length, _assetNameComparer);

            for (int i = 0; i < assetInfos.Length; i++)
            {
                AssetInfo assetInfo = assetInfos[i];

                //Log.Info($"Caching assets: {i + 1}/{assetInfos.Length}");

                // Assets in the "root" seem to be just template assets, so don't include them in the cache
                if (assetInfo.Key.IndexOf('/') == -1)
                    continue;

                string assetName = assetInfo.ObjectName;

                if (string.IsNullOrEmpty(assetName))
                {
                    UnityEngine.Object asset = assetInfo.Asset;
                    if (asset is null || !asset)
                    {
                        Log.Warning($"Null asset at {assetInfo.Key} ({assetInfo.AssetType.Name})");
                        continue;
                    }

                    assetName = asset.name;
                }

                if (string.IsNullOrEmpty(assetName))
                {
                    Log.Warning($"Invalid asset name at {assetInfo.Key} ({assetInfo.AssetType.Name})");
                    continue;
                }

                int assetIndex = validAssetInfos.Count;
                validAssetInfos.Add(assetInfo);

                if (!_objectNameGroupings.TryGetValue(assetName, out AssetNameGrouping assetNameGrouping))
                {
                    assetNameGrouping = new AssetNameGrouping(assetName);
                    _objectNameGroupings.Add(assetName, assetNameGrouping);
                }

                assetNameGrouping.AssetIndices.Add(assetIndex);
            }

            Log.Info($"Split {validAssetInfos.Count} assets into {_objectNameGroupings.Count} named groups");

            _assets = validAssetInfos.ToArray();
        }

        public AssetInfo this[UnityEngine.Object key]
        {
            get
            {
                return TryGetValue(key, out AssetInfo assetInfo) ? assetInfo : throw new KeyNotFoundException($"Key {key} not found");
            }
        }

        public IEnumerable<UnityEngine.Object> Keys => _assets.Select(a => a.Asset);

        public IEnumerable<AssetInfo> Values => _assets;

        public int Count => _assets.Length;

        public bool ContainsKey(UnityEngine.Object key)
        {
            return lookupAsset(key, out _);
        }

        public bool TryGetValue(UnityEngine.Object key, out AssetInfo value)
        {
            if (lookupAsset(key, out int assetIndex) && assetIndex < _assets.Length)
            {
                value = _assets[assetIndex];
                return true;
            }

            value = default;
            return false;
        }

        public IEnumerator<KeyValuePair<UnityEngine.Object, AssetInfo>> GetEnumerator()
        {
            return _assets.Select(a => new KeyValuePair<UnityEngine.Object, AssetInfo>(a.Asset, a)).GetEnumerator();
        }

        bool lookupAsset(UnityEngine.Object obj, out int assetIndex)
        {
            assetIndex = -1;
            if (!obj)
                return false;

            if (_objectNameGroupings.TryGetValue(obj.name, out AssetNameGrouping grouping))
            {
                foreach (int index in grouping.AssetIndices)
                {
                    if (_assets[index].Asset == obj)
                    {
                        assetIndex = index;
                        return true;
                    }
                }
            }

            return false;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        class AssetNameGrouping
        {
            public string ObjectName { get; }

            public readonly List<int> AssetIndices;

            public AssetNameGrouping(string objectName)
            {
                ObjectName = objectName;
                AssetIndices = [];
            }
        }
    }
}
