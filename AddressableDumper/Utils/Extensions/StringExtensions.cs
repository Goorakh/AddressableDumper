using System;
using System.Text;

namespace AddressableDumper.Utils.Extensions
{
    public static class StringExtensions
    {
        static readonly StringBuilder _sharedStringBuilder = new StringBuilder();

        public static int FastIndexOfAny(this string str, char[] orderedChars)
        {
            return FastIndexOfAny(str, orderedChars, 0, str.Length);
        }

        public static int FastIndexOfAny(this string str, char[] orderedChars, int startIndex)
        {
            return FastIndexOfAny(str, orderedChars, startIndex, str.Length - startIndex);
        }

        public static int FastIndexOfAny(this string str, char[] orderedChars, int startIndex, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (Array.BinarySearch(orderedChars, str[startIndex + i]) >= 0)
                {
                    return startIndex + i;
                }
            }

            return -1;
        }

        public static string FilterCharsFast(this string str, char[] orderedInvalidChars)
        {
            return FilterCharsFast(str, orderedInvalidChars, 0, str.Length);
        }

        public static string FilterCharsFast(this string str, char[] orderedInvalidChars, int startIndex)
        {
            return FilterCharsFast(str, orderedInvalidChars, startIndex, str.Length - startIndex);
        }

        public static string FilterCharsFast(this string str, char[] orderedInvalidChars, int startIndex, int count)
        {
            return ReplaceCharsFast(str, orderedInvalidChars, string.Empty, startIndex, count);
        }

        public static string ReplaceCharsFast(this string str, char[] orderedReplaceChars, char replaceWith)
        {
            return ReplaceCharsFast(str, orderedReplaceChars, replaceWith, 0, str.Length);
        }

        public static string ReplaceCharsFast(this string str, char[] orderedReplaceChars, char replaceWith, int startIndex)
        {
            return ReplaceCharsFast(str, orderedReplaceChars, replaceWith, startIndex, str.Length - startIndex);
        }

        public static string ReplaceCharsFast(this string str, char[] orderedReplaceChars, char replaceWith, int startIndex, int count)
        {
            return ReplaceCharsFast(str, orderedReplaceChars, replaceWith.ToString(), startIndex, count);
        }

        public static string ReplaceCharsFast(this string str, char[] orderedReplaceChars, string replaceWith)
        {
            return ReplaceCharsFast(str, orderedReplaceChars, replaceWith, 0, str.Length);
        }

        public static string ReplaceCharsFast(this string str, char[] orderedReplaceChars, string replaceWith, int startIndex)
        {
            return ReplaceCharsFast(str, orderedReplaceChars, replaceWith, startIndex, str.Length - startIndex);
        }

        public static string ReplaceCharsFast(this string str, char[] orderedReplaceChars, string replaceWith, int startIndex, int count)
        {
            if (orderedReplaceChars is null || orderedReplaceChars.Length == 0)
                return str;

            int firstFilteredCharIndex = str.FastIndexOfAny(orderedReplaceChars, startIndex, count);
            if (firstFilteredCharIndex == -1)
                return str;

            _sharedStringBuilder.Clear();
            _sharedStringBuilder.EnsureCapacity(str.Length);

            if (firstFilteredCharIndex > 0)
            {
                _sharedStringBuilder.Append(str, 0, firstFilteredCharIndex);
            }

            count -= firstFilteredCharIndex - startIndex + 1;
            startIndex = firstFilteredCharIndex + 1;

            // Replace first index
            _sharedStringBuilder.Append(replaceWith);

            for (int i = 0; i < count; i++)
            {
                char c = str[startIndex + i];

                if (Array.BinarySearch(orderedReplaceChars, c) >= 0)
                {
                    _sharedStringBuilder.Append(replaceWith);
                }
                else
                {
                    _sharedStringBuilder.Append(c);
                }
            }

            return _sharedStringBuilder.ToString();
        }

        public static string FilterChars(this string str, char[] invalidChars)
        {
            return FilterChars(str, invalidChars, 0, str.Length);
        }

        public static string FilterChars(this string str, char[] invalidChars, int startIndex)
        {
            return FilterChars(str, invalidChars, startIndex, str.Length - startIndex);
        }

        public static string FilterChars(this string str, char[] invalidChars, int startIndex, int count)
        {
            return ReplaceChars(str, invalidChars, string.Empty, startIndex, count);
        }

        public static string ReplaceChars(this string str, char[] replaceChars, char replaceWith)
        {
            return ReplaceChars(str, replaceChars, replaceWith, 0, str.Length);
        }

        public static string ReplaceChars(this string str, char[] replaceChars, char replaceWith, int startIndex)
        {
            return ReplaceChars(str, replaceChars, replaceWith, startIndex, str.Length - startIndex);
        }

        public static string ReplaceChars(this string str, char[] replaceChars, char replaceWith, int startIndex, int count)
        {
            return ReplaceChars(str, replaceChars, replaceWith.ToString(), startIndex, count);
        }

        public static string ReplaceChars(this string str, char[] replaceChars, string replaceWith)
        {
            return ReplaceChars(str, replaceChars, replaceWith, 0, str.Length);
        }

        public static string ReplaceChars(this string str, char[] replaceChars, string replaceWith, int startIndex)
        {
            return ReplaceChars(str, replaceChars, replaceWith, startIndex, str.Length - startIndex);
        }

        public static string ReplaceChars(this string str, char[] replaceChars, string replaceWith, int startIndex, int count)
        {
            if (replaceChars is null || replaceChars.Length == 0)
                return str;

            int firstFilteredCharIndex = str.IndexOfAny(replaceChars, startIndex, count);
            if (firstFilteredCharIndex == -1)
                return str;

            _sharedStringBuilder.Clear();
            _sharedStringBuilder.EnsureCapacity(str.Length);

            if (firstFilteredCharIndex > 0)
            {
                _sharedStringBuilder.Append(str, 0, firstFilteredCharIndex);
            }

            count -= firstFilteredCharIndex - startIndex + 1;
            startIndex = firstFilteredCharIndex + 1;

            // Replace first index
            _sharedStringBuilder.Append(replaceWith);

            for (int i = 0; i < count; i++)
            {
                char c = str[startIndex + i];

                if (Array.IndexOf(replaceChars, c) >= 0)
                {
                    _sharedStringBuilder.Append(replaceWith);
                }
                else
                {
                    _sharedStringBuilder.Append(c);
                }
            }

            return _sharedStringBuilder.ToString();
        }
    }
}
