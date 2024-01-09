using System;
using UnityEngine.AddressableAssets;

namespace AddressableDumper
{
    public readonly struct AssetInfo
    {
        public readonly string Key;

        public UnityEngine.Object Asset => Addressables.LoadAssetAsync<UnityEngine.Object>(Key).WaitForCompletion();

        public Type AssetType => Asset?.GetType();

        public AssetInfo(string key)
        {
            Key = key;
        }
    }
}
