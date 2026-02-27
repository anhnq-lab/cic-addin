// Polyfill for .NET Core APIs not available on .NET Framework 4.8
// GetValueOrDefault<K,V> and Enum.Parse<T> support

#if !NET5_0_OR_GREATER
using System.Collections.Generic;

namespace CIC.BIM.Addin.Analytics
{
    internal static class NetFrameworkPolyfills
    {
        /// <summary>
        /// Dictionary.GetValueOrDefault polyfill for .NET Framework 4.8
        /// </summary>
        public static TValue GetValueOrDefault<TKey, TValue>(
            this IDictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue = default!)
        {
            return dictionary.TryGetValue(key, out var value) ? value : defaultValue;
        }
    }
}
#endif
