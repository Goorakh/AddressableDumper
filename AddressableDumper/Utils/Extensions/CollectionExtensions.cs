using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AddressableDumper.Utils.Extensions
{
    public static class CollectionExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureCapacity<T>(this List<T> list, int capacity)
        {
            if (list.Capacity < capacity)
            {
                list.Capacity = capacity;
            }
        }
    }
}
