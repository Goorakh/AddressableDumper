using System;
using System.Text;
using UnityEngine;

namespace AddressableDumper.Utils
{
    public static class TransformUtils
    {
        public static string GetPath(Transform from, Transform to)
        {
            if (!from)
                throw new ArgumentNullException(nameof(from));

            if (!to)
                throw new ArgumentNullException(nameof(to));

            if (from.root != to.root)
                throw new ArgumentException("Transforms must share a root");

            StringBuilder pathBuilder = HG.StringBuilderPool.RentStringBuilder();

            int ascendCount = 0;
            Transform sharedParent = from;
            while (!to.IsChildOf(sharedParent))
            {
                ascendCount++;
                sharedParent = sharedParent.parent;
            }

            for (Transform current = to; current != sharedParent; current = current.parent)
            {
                pathBuilder.Insert(0, $"/{current.name}");
            }

            pathBuilder.Insert(0, sharedParent.name);

            for (int i = 0; i < ascendCount; i++)
            {
                pathBuilder.Insert(0, "../");
            }

            string result = pathBuilder.ToString();
            HG.StringBuilderPool.ReturnStringBuilder(pathBuilder);

            return result;
        }
    }
}
