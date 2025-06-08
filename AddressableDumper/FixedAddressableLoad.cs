using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace AddressableDumper
{
    public static class FixedAddressableLoad
    {
        static readonly MethodInfo _loadAssetAsyncLocatorMethod = typeof(Addressables).GetMethod(nameof(Addressables.LoadAssetAsync), BindingFlags.Public | BindingFlags.Static, null, [typeof(IResourceLocation)], null);

        static readonly Dictionary<Type, MethodInfo> _genericLoadAssetMethodsCache = [];

        public static UnityEngine.Object LoadAsset(IResourceLocation location)
        {
            if (!_genericLoadAssetMethodsCache.TryGetValue(location.ResourceType, out MethodInfo loadAssetMethod))
            {
                loadAssetMethod = _loadAssetAsyncLocatorMethod.MakeGenericMethod(location.ResourceType);
                _genericLoadAssetMethodsCache.Add(location.ResourceType, loadAssetMethod);
            }

            object asyncOperationHandle = loadAssetMethod.Invoke(null, [location]);

            MethodInfo waitForCompletionMethod = asyncOperationHandle.GetType().GetMethod("WaitForCompletion");

            return (UnityEngine.Object)waitForCompletionMethod.Invoke(asyncOperationHandle, null);
        }

        public static UnityEngine.Object LoadAsset(object key, Type type)
        {
            foreach (IResourceLocator locator in Addressables.ResourceLocators)
            {
                if (locator.Locate(key, type, out IList<IResourceLocation> locations) && locations.Count > 0)
                {
                    return LoadAsset(locations[0]);
                }
            }

            return null;
        }
    }
}
