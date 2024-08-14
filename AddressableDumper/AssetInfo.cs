using System;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace AddressableDumper
{
    public readonly struct AssetInfo
    {
        public readonly IResourceLocation Location;

        public readonly string Key;
        public readonly Type AssetType;

        public UnityEngine.Object Asset => FixedAddressableLoad.LoadAsset(Location);

        public AssetInfo(IResourceLocation location)
        {
            Location = location;

            Key = Location.PrimaryKey;
            AssetType = Location.ResourceType;
        }

        public override string ToString()
        {
            return Key;
        }
    }
}
