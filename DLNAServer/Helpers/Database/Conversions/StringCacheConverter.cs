using DLNAServer.Common;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;

namespace DLNAServer.Helpers.Database.Conversions
{
    public sealed class StringCacheConverter : ValueConverter<string?, string?>
    {
        private static readonly MemoryCache _cache = new(
            new MemoryCacheOptions()
            {
                Clock = new SystemClock(),
                ExpirationScanFrequency = TimeSpanValues.TimeMin1,
                TrackStatistics = true,
                SizeLimit = 32 * 1024 * 1024,
            });
        private readonly static MemoryCacheEntryOptions memoryCacheEntryOptions = new()
        {
            SlidingExpiration = TimeSpanValues.TimeHours1,
            AbsoluteExpirationRelativeToNow = TimeSpanValues.TimeDays1
        };
        private const int charSize = sizeof(char); // 1 char = 2 bytes by default
        public StringCacheConverter()
            : base(
                  convertToProviderExpression: static (value) => StringCache(value),
                  convertFromProviderExpression: static (value) => StringCache(value))
        {
        }
        private static string? StringCache(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            ulong key = GetFnv1aHash(value);

            if (_cache.TryGetValue(key, out string? cached))
            {
                return cached;
            }

            LastAddCache = DateTime.Now;

            return _cache.Set(
                key,
                value,
                memoryCacheEntryOptions
                    .SetSize(value.Length * charSize));
        }

        private static ulong GetFnv1aHash(ReadOnlySpan<char> value)
        {
            ulong hash = 2166136261;  // FNV-1a initial value
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619;  // FNV-1a prime
            }
            return hash;
        }
        /// <summary> 
        /// Returns a cached instance of the given string to reduce memory allocations.<br/>
        /// Using this here is acceptable for quick reuse, but note that exposing cache logic<br/>
        /// from a persistence-specific converter couples unrelated concerns and reduces reusability.<br/>
        /// </summary>
        /// <remarks>
        /// <b>Very bad practise:</b><br/>
        /// Couples persistence-specific converter with global caching logic<br/>
        /// Break separation of concerns, reduces reusability and it is depending on the EF-layer
        /// </remarks>
        public static string? CacheString(string? value)
        {
            return StringCache(value);
        }
        public static int? CacheCount => _cache?.Count;
        public static DateTime? LastAddCache { get; private set; }
        public static long? CurrentEstimatedSize => _cache?.GetCurrentStatistics()?.CurrentEstimatedSize;
        public static long? CurrentEntryCount => _cache?.GetCurrentStatistics()?.CurrentEntryCount;
        public static long? TotalHits => _cache?.GetCurrentStatistics()?.TotalHits;
        public static long? TotalMisses => _cache?.GetCurrentStatistics()?.TotalMisses;
    }
}
