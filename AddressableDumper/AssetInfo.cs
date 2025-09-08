using System;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace AddressableDumper
{
    public class AssetInfo : IComparable<AssetInfo>
    {
        public readonly IResourceLocation Location;

        public readonly string ObjectName;

        public readonly AssetInfo[] SubAssets;

        bool _hasCachedAsset;
        UnityEngine.Object _cachedAsset;

        public UnityEngine.Object Asset
        {
            get
            {
                if (!_hasCachedAsset)
                {
                    _cachedAsset = FixedAddressableLoad.LoadAsset(Location);
                    _hasCachedAsset = true;
                }

                return _cachedAsset;
            }
        }

        public string Key => Location?.PrimaryKey ?? string.Empty;

        public Type AssetType => Location?.ResourceType;

        public AssetInfo(IResourceLocation location, string objectName, AssetInfo[] subAssets)
        {
            Location = location;

            ObjectName = objectName;

            SubAssets = subAssets;
        }

        public AssetInfo(IResourceLocation location, AssetInfo[] subAssets)
        {
            Location = location;

            string objectName = string.Empty;
            if (Asset)
            {
                objectName = Asset.name;
            }

            ObjectName = objectName;

            SubAssets = subAssets;
        }

        public override string ToString()
        {
            return $"{Key} ({AssetType?.FullName})";
        }

        public int CompareTo(AssetInfo other)
        {
            if (other is null)
                return 1;

            int keyComparison = string.Compare(Key, other.Key);
            if (keyComparison != 0)
                return keyComparison;

            return string.Compare(AssetType.AssemblyQualifiedName, other.AssetType.AssemblyQualifiedName);
        }
    }
}
