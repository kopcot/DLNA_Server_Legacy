using DLNAServer.Features.Cache.Interfaces;
using System.Threading.Channels;

namespace DLNAServer.Features.Cache
{
    public class FileMemoryCacheHandler : IFileMemoryCacheHandler
    {
        private readonly Channel<(Guid fileID, TimeSpan slidingExpiration, DateTime eventTimeUTC)> _fileMemoryCacheChannel = Channel.CreateUnbounded<(Guid, TimeSpan, DateTime)>();
        public ChannelReader<(Guid fileID, TimeSpan slidingExpiration, DateTime eventTimeUTC)> FileMemoryCacheChannelReader => _fileMemoryCacheChannel.Reader;

        public bool TryAddFileToCache(Guid fileID, TimeSpan slidingExpiration) =>
            _fileMemoryCacheChannel.Writer.TryWrite((
                fileID,
                slidingExpiration,
                eventTimeUTC: DateTime.UtcNow
                ));
        public Task TerminateAsync()
        {
            // unable to Channel.Writer.Complete() for restarting possibility of the server
            _fileMemoryCacheChannel.Writer.Complete();

            return Task.CompletedTask;
        }

    }
}
