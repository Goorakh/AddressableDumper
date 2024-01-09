using System;
using System.Collections.Generic;
using System.Text;

namespace AddressableDumper.Utils.Extensions
{
    public static class StringExtensions
    {
        static readonly StringBuilder _sharedStringBuilder = new StringBuilder();

        public static string FilterChars(this string str, char[] invalidChars)
        {
            if (invalidChars is null || invalidChars.Length == 0)
                return str;

            int startIndex = str.IndexOfAny(invalidChars);
            if (startIndex == -1)
                return str;

            _sharedStringBuilder.Clear();

            if (startIndex > 0)
            {
                _sharedStringBuilder.Append(str, 0, startIndex);
            }

            for (int i = startIndex + 1; i < str.Length; i++)
            {
                char c = str[i];

                if (Array.IndexOf(invalidChars, c) == -1)
                {
                    _sharedStringBuilder.Append(c);
                }
            }

            return _sharedStringBuilder.ToString();
        }
    }
}
