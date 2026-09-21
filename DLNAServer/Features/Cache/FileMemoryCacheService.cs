using DLNAServer.Database.Repositories.Interfaces;
using DLNAServer.Features.Cache.Interfaces;
using DLNAServer.Helpers.Logger;

namespace DLNAServer.Features.Cache
{
    public partial class FileMemoryCacheService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<FileMemoryCacheService> _logger;
        private readonly IFileMemoryCacheHandler _fileMemoryCacheHandler;
        public FileMemoryCacheService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<FileMemoryCacheService> logger,
            IFileMemoryCacheHandler fileMemoryCacheHandler)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _fileMemoryCacheHandler = fileMemoryCacheHandler;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                InformationStarting();

                var reader = _fileMemoryCacheHandler.FileMemoryCacheChannelReader;

                Guid guid;

                while (await reader.WaitToReadAsync(stoppingToken))
                {
                    guid = Guid.NewGuid();

                    InformationStartedCheckingRaisedEvents(guid);

                    using (var scope = _serviceScopeFactory.CreateScope())
                    {
                        var fileMemoryCacheManager = scope.ServiceProvider.GetRequiredService<IFileMemoryCacheManager>();
                        var fileRepository = scope.ServiceProvider.GetRequiredService<IFileRepository>();

                        while (reader.TryRead(out var fileEvent))
                        {
                            if (stoppingToken.IsCancellationRequested)
                            {
                                LoggerHelper.InformationCancellationRequested(_logger, $"Caching file: {fileEvent.fileID}");
                                stoppingToken.ThrowIfCancellationRequested();
                            }

                            DebugActualRaisedEvent(fileEvent.fileID, reader.CanCount ? reader.Count : -999);

                            var file = await fileRepository.GetByIdAsync(fileEvent.fileID, asNoTracking: true, useCachedResult: true);
                            if (file == null)
                            {
                                WarningMissingFileEntityWithID(fileEvent.fileID);
                                continue;
                            }

                            (var isCachedSuccessful, _) = await fileMemoryCacheManager.CacheFileAndReturnAsync(
                                file.FilePhysicalFullPath, 
                                fileEvent.slidingExpiration, 
                                true,
                                stoppingToken);

                            if (file.FileUnableToCache != !isCachedSuccessful)
                            {
                                var fileCached = await fileRepository.GetByIdAsync(file.Id, asNoTracking: false, useCachedResult: false);
                                fileCached!.FileUnableToCache = !isCachedSuccessful;
                                _ = await fileRepository.SaveChangesAsync();
                            }
                        }
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
    }
}
