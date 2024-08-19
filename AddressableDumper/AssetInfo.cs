using System;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace AddressableDumper
{
    public readonly struct AssetInfo
    {
        public readonly IResourceLocation Location;

        public readonly string ObjectName;

        public readonly string Key;
        public readonly Type AssetType;

        public UnityEngine.Object Asset => FixedAddressableLoad.LoadAsset(Location);

        public AssetInfo(IResourceLocation location, string objectName)
        {
            Location = location;

            ObjectName = objectName;

            Key = Location.PrimaryKey;
            AssetType = Location.ResourceType;
        }

        public AssetInfo(IResourceLocation location) : this(location, FixedAddressableLoad.LoadAsset(location)?.name)
        {
        }

        public override string ToString()
        {
            return Key;
        }
    }
}
