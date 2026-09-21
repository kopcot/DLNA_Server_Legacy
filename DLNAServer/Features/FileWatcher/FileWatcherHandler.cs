using DLNAServer.Common;
using DLNAServer.Features.FileWatcher.Interfaces;
using DLNAServer.Helpers.Database.Conversions;
using DLNAServer.Helpers.Logger;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DLNAServer.Features.FileWatcher
{
    public partial class FileWatcherHandler : IFileWatcherHandler
    {
        private readonly ILogger<FileWatcherHandler> _logger;
        private readonly static ConcurrentDictionary<string, FileSystemWatcher> _fileSystemWatchers = new();
        private readonly Channel<(string fileFullPath, string? fileFullPathOld, WatcherChangeTypes changeType, DateTime eventTimeUTC)> _fileEventChannel =
            Channel.CreateUnbounded<(string, string?, WatcherChangeTypes, DateTime)>(
                new UnboundedChannelOptions
                {
                    SingleWriter = false,
                    SingleReader = true,
                });
        public FileWatcherHandler(
            ILogger<FileWatcherHandler> logger
            )
        {
            _logger = logger;
        }
        public void WatchPath(string pathToWatch)
        {
            if (_fileSystemWatchers.ContainsKey(pathToWatch))
            {
                WarningPathAlreadyWatching(pathToWatch);
                return;
            }

            if (!Directory.Exists(pathToWatch))
            {
                WarningDirectoryNotExists(pathToWatch);
                return;
            }

            FileSystemWatcher watcher = new(pathToWatch)
            {
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    //| NotifyFilters.Attributes 
                    //| NotifyFilters.Size
                    | NotifyFilters.LastWrite
                    //| NotifyFilters.LastAccess 
                    //| NotifyFilters.CreationTime
                    //| NotifyFilters.Security
                    ,
                EnableRaisingEvents = true,
                IncludeSubdirectories = true,
                InternalBufferSize = 1_024 * 1_024,
            };

            // cannot be done, for Linux it is different file if it is .jpg, .JPG or .Jpg
            //ServerConfig.Extensions.ToList().ForEach(ex => watcher.Filters.Add("*" + ex.Key)); 

            watcher.Created += (_, args) =>
            {
                DebugRaisedEvent(pathToWatch, args.FullPath, args.ChangeType);
                TryWriteUnique(
                    fileFullPath: args.FullPath,
                    fileFullPathOld: null,
                    changeType: WatcherChangeTypes.Created);
            };
            watcher.Changed += (_, args) =>
            {
                DebugRaisedEvent(pathToWatch, args.FullPath, args.ChangeType);
                TryWriteUnique(
                    fileFullPath: args.FullPath,
                    fileFullPathOld: null,
                    changeType: WatcherChangeTypes.Changed);
            };
            watcher.Renamed += (_, args) =>
            {
                DebugRaisedEvent(pathToWatch, args.FullPath, args.ChangeType);
                TryWriteUnique(
                    fileFullPath: args.FullPath,
                    fileFullPathOld: args.OldFullPath,
                    changeType: WatcherChangeTypes.Renamed);
            };
            watcher.Deleted += (_, args) =>
            {
                DebugRaisedEvent(pathToWatch, args.FullPath, args.ChangeType);
                TryWriteUnique(
                    fileFullPath: args.FullPath,
                    fileFullPathOld: null,
                    changeType: WatcherChangeTypes.Deleted);
            };
            watcher.Error += (_, args) =>
            {
                _logger.LogGeneralErrorMessage(args.GetException(), additionalMessage: $"Raised Error event from watched path '{pathToWatch}'");
            };

            _ = _fileSystemWatchers.TryAdd(pathToWatch, watcher);

            DebugStartedWatchingPath(pathToWatch);
        }
        public void EnableRaisingEvents(bool enable)
        {
            foreach (var watcher in _fileSystemWatchers)
            {
                watcher.Value.EnableRaisingEvents = enable;
            }
        }
        private (string fileFullPath, string? fileFullPathOld, WatcherChangeTypes changeType, DateTime eventTimeUTC)? _lastEvent;
        private readonly object _lastEventLock = new();
        private void TryWriteUnique(
            string fileFullPath,
            string? fileFullPathOld,
            WatcherChangeTypes changeType
            )
        {
            lock (_lastEventLock)
            {
                if (_lastEvent != null &&
                    _lastEvent.HasValue &&
                    string.Equals(_lastEvent.Value.fileFullPath, fileFullPath, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(_lastEvent.Value.fileFullPathOld, fileFullPathOld, StringComparison.OrdinalIgnoreCase) &&
                    _lastEvent.Value.changeType == changeType &&
                    (_lastEvent.Value.eventTimeUTC - DateTime.UtcNow) < TimeSpanValues.TimeMs100
                    )
                {
                    // DUPLICATE EVENT → ignore
                    return;
                }

                _lastEvent = (
                    StringCacheConverter.CacheString(fileFullPath)!,
                    StringCacheConverter.CacheString(fileFullPathOld),
                    changeType,
                    DateTime.UtcNow);

                _fileEventChannel.Writer.TryWrite(_lastEvent.Value);
            }
        }

        private static void UnwatchPath(string pathToWatch)
        {
            if (_fileSystemWatchers.TryRemove(pathToWatch, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
        }
        public Task TerminateAsync()
        {
            foreach (var watcher in _fileSystemWatchers)
            {
                UnwatchPath(watcher.Key);
            }

            _fileSystemWatchers.Clear();

            _fileEventChannel.Writer.Complete();

            return Task.CompletedTask;
        }
        public ChannelReader<(string fileFullPath, string? fileFullPathOld, WatcherChangeTypes changeType, DateTime eventTimeUTC)> FileEventChannelReader => _fileEventChannel.Reader;
    }
}
