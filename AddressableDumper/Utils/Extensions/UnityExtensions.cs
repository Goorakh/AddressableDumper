using UnityEngine;

namespace AddressableDumper.Utils.Extensions
{
    static class UnityExtensions
    {
        public static GameObject GetGameObject(this UnityEngine.Object obj)
        {
            return obj switch
            {
                GameObject gameObject => gameObject,
                Component component => component.gameObject,
                _ => null
            };
        }
    }
}
