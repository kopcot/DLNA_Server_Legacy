using DLNAServer.Helpers.Interfaces;

namespace DLNAServer.Features.Cache.Interfaces
{
    public interface IFileMemoryCacheManager : ITerminateAble
    {
        Task<(bool isCachedSuccessful, ReadOnlyMemory<byte> file)> CacheFileAndReturnAsync(
            string filePath,
            TimeSpan slidingExpiration,
            bool checkExistingInCache = true,
            CancellationToken cancellationToken = default);
        (bool isCached, ReadOnlyMemory<byte> file) GetCheckCachedFile(string filePath, TimeSpan slidingExpiration);
        void EvictSingleFile(string filePath);
    }
}
