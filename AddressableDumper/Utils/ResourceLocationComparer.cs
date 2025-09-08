using System;
using System.Collections.Generic;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace AddressableDumper.Utils
{
    public sealed class ResourceLocationComparer : IEqualityComparer<IResourceLocation>
    {
        public static ResourceLocationComparer Instance { get; } = new ResourceLocationComparer();

        public bool Equals(IResourceLocation x, IResourceLocation y)
        {
            return ReferenceEquals(x, y) ||
                   (x != null &&
                    y != null &&
                    x.PrimaryKey == y.PrimaryKey &&
                    x.ResourceType == y.ResourceType);
        }

        public int GetHashCode(IResourceLocation obj)
        {
            return obj.GetHashCode();
        }
    }
}
