using DLNAServer.Common;
using DLNAServer.Configuration;
using DLNAServer.Features.Cache.Interfaces;
using DLNAServer.Features.PhysicalFile.Interfaces;
using DLNAServer.Helpers.Caching;
using DLNAServer.Helpers.Logger;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Runtime;

namespace DLNAServer.Features.Cache
{
    public partial class FileMemoryCacheManager : IFileMemoryCacheManager
    {
        private readonly ILogger<FileMemoryCacheManager> _logger;
        private readonly ServerConfig _serverConfig;
        private readonly IMemoryCache MemoryCache;
        private readonly IFileService FileService;
        private readonly static ConcurrentDictionary<string, SemaphoreSlim> cachingFilesInProgress = new();
        private readonly static SemaphoreSlim postEvictionCallbackInProgress = new(1, 1);
        private readonly static TimeSpan _defaultExpiration = TimeSpanValues.TimeMin1;

        public FileMemoryCacheManager(
            ServerConfig serverConfig,
            IMemoryCache memoryCache,
            ILogger<FileMemoryCacheManager> logger,
            IFileService fileService)
        {
            MemoryCache = memoryCache;
            _logger = logger;
            _serverConfig = serverConfig;
            FileService = fileService;
        }
        public async Task<(bool isCachedSuccessful, ReadOnlyMemory<byte> file)> CacheFileAndReturnAsync(
            string filePath,
            TimeSpan slidingExpiration,
            bool checkExistingInCache = true,
            CancellationToken cancellationToken = default)
        {
            slidingExpiration = slidingExpiration > _defaultExpiration
                ? slidingExpiration
                : _defaultExpiration;

            var fileLock = cachingFilesInProgress.GetOrAdd(filePath, new SemaphoreSlim(1, 1));

            DebugFileCacheStarted(filePath);
            _ = await fileLock.WaitAsync(TimeSpanValues.TimeMin30);

            try
            {
                if (checkExistingInCache)
                {
                    (bool isCached, ReadOnlyMemory<byte> file) = GetCheckCachedFile(filePath, slidingExpiration);
                    if (isCached)
                    {
                        DebugFileCacheBefore(filePath);
                        return (isCached, file);
                    }
                }

                FileInfo fileInfo = new(filePath);
                if (!fileInfo.Exists || fileInfo.Length == 0)
                {
                    return (false, ReadOnlyMemory<byte>.Empty);
                }
                var cachedData = await FileService.ReadFileAsync(
                    filePath, 
                    (long)_serverConfig.MaxSizeOfFileForUseMemoryCacheInMBytes * (1024 * 1024),
                    cancellationToken);
                if (cachedData == null)
                {
                    return (false, ReadOnlyMemory<byte>.Empty);
                }

                CacheFileData(filePath, slidingExpiration, cachedData.Value);

                return (true, cachedData.Value);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                return (false, ReadOnlyMemory<byte>.Empty);
            }
            finally
            {
                _ = fileLock.Release();

                DebugFileCacheFinished(filePath);

                _ = cachingFilesInProgress.Remove(filePath, out _);
            }
        }
        private void CacheFileData(string filePath, TimeSpan slidingExpiration, ReadOnlyMemory<byte> cachedData)
        {
            try
            {
                _ = MemoryCache.Set(GetFileCachedKey(filePath), cachedData, EntryOptions(cachedData.Length, slidingExpiration));

                MemoryCache.ScheduleCacheKeyEviction(
                    GetFileCachedKey(filePath),
                    // doubled slidingExpiration for streaming file
                    slidingExpiration.Add(slidingExpiration),
                    _logger);

                DebugFileCacheDone(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }
        public (bool isCached, ReadOnlyMemory<byte> file) GetCheckCachedFile(string filePath, TimeSpan slidingExpiration)
        {
            try
            {
                if (MemoryCache.TryGetValue(GetFileCachedKey(filePath), out ReadOnlyMemory<byte>? fileMemoryByte)
                    && fileMemoryByte != null
                    && fileMemoryByte.HasValue)
                {
                    MemoryCache.ScheduleCacheKeyEviction(
                        GetFileCachedKey(filePath),
                        // doubled slidingExpiration for streaming file
                        slidingExpiration.Add(slidingExpiration),
                        _logger);

                    return (true, fileMemoryByte.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
            return (false, ReadOnlyMemory<byte>.Empty);
        }

        private MemoryCacheEntryOptions EntryOptions(long size, TimeSpan slidingExpiration)
        {
            return new MemoryCacheEntryOptions()
            {
                Size = size,
                SlidingExpiration = slidingExpiration,
                AbsoluteExpirationRelativeToNow = TimeSpanValues.TimeHours12,
                Priority = CacheItemPriority.Low,
            }
            .RegisterPostEvictionCallback((key, _, _, _) =>
            {
                MemoryCache.CancelCacheKeyEviction((string)key, _logger);

                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

                _ = Task.Run(ClearGC);
            });
        }
        private static async Task ClearGC()
        {
            await Task.Delay(TimeSpanValues.TimeSecs30);

            _ = await postEvictionCallbackInProgress.WaitAsync(TimeSpanValues.TimeMin30);

            await Task.Delay(TimeSpanValues.TimeSecs1);

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();
            GC.Collect();

            await Task.Delay(TimeSpanValues.TimeSecs1);

            _ = postEvictionCallbackInProgress.Release();
        }
        public void EvictSingleFile(string filePath)
        {
            try
            {
                string cachedKey = GetFileCachedKey(filePath);

                MemoryCache.Remove(cachedKey);

                MemoryCache.CancelCacheKeyEviction(cachedKey, _logger);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }
        private static string GetFileCachedKey(string filePath)
        {
            return string.Format("{0} {1} {2} {3}", [nameof(FileMemoryCacheManager), nameof(CacheFileData), typeof(byte[]).Name, filePath]);
        }
        public Task TerminateAsync()
        {
            foreach (var cachingFile in cachingFilesInProgress)
            {
                cachingFile.Value.Dispose();
            }

            cachingFilesInProgress.Clear();

            if (MemoryCache is MemoryCache memoryCache)
            {
                memoryCache.Compact(100);
                memoryCache.Clear();
                memoryCache.Compact(100);
            }
            MemoryCache.Dispose();

            return Task.CompletedTask;
        }
    }
}
