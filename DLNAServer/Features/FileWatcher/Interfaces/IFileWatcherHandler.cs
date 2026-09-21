using DLNAServer.Helpers.Interfaces;
using System.Threading.Channels;

namespace DLNAServer.Features.FileWatcher.Interfaces
{
    public interface IFileWatcherHandler : ITerminateAble
    {
        void WatchPath(string pathToWatch);
        void EnableRaisingEvents(bool enable);
        ChannelReader<(string fileFullPath, string? fileFullPathOld, WatcherChangeTypes changeType, DateTime eventTimeUTC)> FileEventChannelReader { get; }
    }
}
