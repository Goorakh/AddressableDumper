using System;
using System.IO;

namespace AddressableDumper.Utils
{
    public static class PathUtils
    {
        public static readonly char[] OrderedInvalidFileNameChars;

        public static readonly char[] OrderedInvalidPathChars;

        static PathUtils()
        {
            OrderedInvalidFileNameChars = Path.GetInvalidFileNameChars();
            Array.Sort(OrderedInvalidFileNameChars);

            OrderedInvalidPathChars = Path.GetInvalidPathChars();
            Array.Sort(OrderedInvalidPathChars);
        }
    }
}
