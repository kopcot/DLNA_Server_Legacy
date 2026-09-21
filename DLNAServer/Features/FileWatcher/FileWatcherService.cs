using DLNAServer.Common;
using DLNAServer.Configuration;
using DLNAServer.Features.FileWatcher.Interfaces;
using DLNAServer.Helpers.Logger;

namespace DLNAServer.Features.FileWatcher
{
    public partial class FileWatcherService : BackgroundService
    {
        private readonly ILogger<FileWatcherService> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ServerConfig _serverConfig;
        private readonly IFileWatcherHandler _fileWatcherHandler;
        public FileWatcherService(
            ILogger<FileWatcherService> logger,
            ServerConfig serverConfig,
            IServiceScopeFactory serviceScopeFactory,
            IFileWatcherHandler fileWatcherHandler
            )
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _serverConfig = serverConfig;
            _fileWatcherHandler = fileWatcherHandler;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                InformationStarting();

                var reader = _fileWatcherHandler.FileEventChannelReader;

                Guid guid;

                while (await reader.WaitToReadAsync(stoppingToken))
                {
                    guid = Guid.NewGuid();

                    InformationStartedCheckingRaisedEvents(guid);

                    while (reader.TryRead(out var fileEvent))
                    {
                        if (stoppingToken.IsCancellationRequested)
                        {
                            LoggerHelper.InformationCancellationRequested(_logger, $"File full path: {fileEvent.fileFullPath}");
                            stoppingToken.ThrowIfCancellationRequested();
                        }
                        DebugActualRaisedEvent(fileEvent.fileFullPath, fileEvent.changeType, reader.CanCount ? reader.Count : -999);

                        var eventStartedTime = DateTime.UtcNow - fileEvent.eventTimeUTC;
                        if (eventStartedTime < TimeSpanValues.TimeSecs30)
                        {
                            await Task.Delay(TimeSpanValues.TimeSecs30, stoppingToken);
                        }
                        await ExecuteEventHandlerAsync(fileEvent.fileFullPath, fileEvent.fileFullPathOld, fileEvent.changeType, stoppingToken);
                    }

                    InformationFinishedActiveRaisedEvents(guid);
                }
            }
            catch (TaskCanceledException)
            {
                //who cares? 
                LoggerHelper.LogWarningTaskCanceled(_logger);
            }
            catch (OperationCanceledException)
            {
                //channel-reader canceled by CancellationToken
                LoggerHelper.LogWarningOperationCanceled(_logger);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }

            InformationFinishedCheckingRaisedEvents();
        }
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await base.StopAsync(cancellationToken);
            }
            catch (TaskCanceledException)
            {
                //who cares? 
                LoggerHelper.LogWarningTaskCanceled(_logger);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }

        private bool ShouldExcludeByThumbnailPath(string fullPath)
        {
            return fullPath.Contains(_serverConfig.SubFolderForThumbnail, StringComparison.OrdinalIgnoreCase);
        }
        private bool ShouldExcludeByExcludeFoldersPath(string fullPath)
        {
            return _serverConfig.ExcludeFolders.Any(exclude => fullPath.Contains(exclude, StringComparison.OrdinalIgnoreCase));
        }
        private bool IsFileExtensionMatch(string fullPath)
        {
            string fileExtension = new FileInfo(fullPath).Extension;
            return _serverConfig.MediaFileExtensions.Any(extension => fileExtension.EndsWith(extension.Key, StringComparison.OrdinalIgnoreCase));
        }
        private static bool IsDirectory(string fullPath)
        {
            return Directory.Exists(fullPath);
        }
        private async Task ExecuteEventHandlerAsync(
            string fullPath,
            string? fullPathOld,
            WatcherChangeTypes changeType,
            CancellationToken cancellationToken
            )
        {
            DateTime eventTimestamp = DateTime.Now;

            if (cancellationToken.IsCancellationRequested)
            {
                LoggerHelper.InformationCancellationRequested(_logger, $"File full path: {fullPath}");
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (CheckPathForExclude(changeType, fullPath))
            {
                return;
            }

            Guid guid = Guid.NewGuid();

            try
            {
                DebugEventStarted(changeType, fullPath, guid);

                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var fileWatcherManager = scope.ServiceProvider.GetRequiredService<IFileWatcherManager>();

                    if (IsFileExtensionMatch(fullPath))
                    {
                        switch (changeType)
                        {
                            case WatcherChangeTypes.Created:
                            case WatcherChangeTypes.Changed:
                                if (!ShouldExcludeByExcludeFoldersPath(fullPath))
                                {
                                    await fileWatcherManager.HandleFileCreatedChanged(fullPath, changeType, eventTimestamp);
                                }
                                break;
                            case WatcherChangeTypes.Renamed:
                                await fileWatcherManager.HandleFileRenamed(fullPath, fullPathOld!, changeType, eventTimestamp);
                                break;
                            case WatcherChangeTypes.Deleted:
                                await fileWatcherManager.HandleFileRemove(fullPath, changeType, eventTimestamp);
                                break;
                        }
                    }
                    else if (IsDirectory(fullPath))
                    {
                        switch (changeType)
                        {
                            case WatcherChangeTypes.Renamed:
                                await fileWatcherManager.HandleDirectoryRenamed(fullPath, fullPathOld!, changeType, eventTimestamp);
                                break;
                            case WatcherChangeTypes.Deleted:
                                await fileWatcherManager.HandleDirectoryRemove(fullPath, changeType, eventTimestamp);
                                break;
                        }
                    }
                }
                DebugEventDone(changeType, fullPath, guid);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }

        private bool CheckPathForExclude(WatcherChangeTypes changeType, string fullPath)
        {
            if (ShouldExcludeByThumbnailPath(fullPath))
            {
                DebugEventFilteredForThumbnailSubfolder(changeType, fullPath);
                return true;
            }

            //if (ShouldExcludeByExcludeFoldersPath(fullPath))
            //{
            //    DebugEventFilteredForExcludeDirectories(changeType, fullPath);
            //    return true;
            //}

            if (!IsFileExtensionMatch(fullPath) && !IsDirectory(fullPath))
            {
                DebugEventFilteredForExtensionOrNotDirectory(changeType, fullPath);
                return true;
            }

            return false;
        }
        public static Task TerminateAsync()
        {
            return Task.CompletedTask;
        }
    }
}
