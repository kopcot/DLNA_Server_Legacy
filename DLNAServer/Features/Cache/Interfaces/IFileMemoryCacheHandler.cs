using DLNAServer.Helpers.Interfaces;
using System.Threading.Channels;

namespace DLNAServer.Features.Cache.Interfaces
{
    public interface IFileMemoryCacheHandler : ITerminateAble
    {
        ChannelReader<(Guid fileID, TimeSpan slidingExpiration, DateTime eventTimeUTC)> FileMemoryCacheChannelReader { get; }
        bool TryAddFileToCache(Guid fileID, TimeSpan slidingExpiration);
    }
}
