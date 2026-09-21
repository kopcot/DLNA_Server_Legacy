using DLNAServer.Common;
using DLNAServer.Configuration;
using DLNAServer.Features.FileWatcher.Interfaces;
using DLNAServer.Features.MediaContent.Interfaces;
using DLNAServer.Helpers.Database;
using DLNAServer.Helpers.Logger;
using DLNAServer.SOAP.Endpoints.Interfaces;
using DLNAServer.SOAP.Endpoints.Responses.ContentDirectory;
using DLNAServer.SOAP.Endpoints.Responses.ContentDirectory.Mapping;

namespace DLNAServer.SOAP.Endpoints
{
    public partial class ContentDirectoryService : IContentDirectoryService
    {
        private readonly Lazy<IContentExplorerManager> _contentExplorerLazy;
        private readonly Lazy<IFileWatcherManager> _fileWatcherManagerLazy;
        private readonly ServerConfig _serverConfig;
        private readonly ILogger<ContentDirectoryService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        private IContentExplorerManager ContentExplorer => _contentExplorerLazy.Value;
        private IFileWatcherManager FileWatcherManager => _fileWatcherManagerLazy.Value;
        private readonly static SemaphoreSlim _browseLock = new(1, 1);

        public ContentDirectoryService(
            Lazy<IContentExplorerManager> contentExplorerLazy,
            Lazy<IFileWatcherManager> fileWatcherManagerLazy,
            ServerConfig serverConfig,
            ILogger<ContentDirectoryService> logger,
            IHttpContextAccessor httpContextAccessor)
        {
            _contentExplorerLazy = contentExplorerLazy;
            _fileWatcherManagerLazy = fileWatcherManagerLazy;
            _serverConfig = serverConfig;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<Browse> Browse(string objectID, string browseFlag, string filter, int startingIndex, int requestedCount, string sortCriteria)
        {
            var connection = _httpContextAccessor.HttpContext?.Connection;

            LoggerHelper.LogDebugConnectionInformation(
                _logger,
                nameof(Browse),
                connection?.RemoteIpAddress,
                connection?.RemotePort,
                connection?.LocalIpAddress,
                connection?.LocalPort,
                _httpContextAccessor.HttpContext?.Request.Path.Value,
                _httpContextAccessor.HttpContext?.Request.Method);
            DebugBrowseRequestInfo(
                nameof(Browse),
                objectID,
                browseFlag,
                filter,
                startingIndex,
                requestedCount,
                sortCriteria);

            var startTime = DateTime.Now;

            Browse? response = null;

            try
            {
                DebugBrowseRequestStart(objectID);

                _ = await _browseLock.WaitAsync(TimeSpanValues.TimeMin1);

                requestedCount = Math.Min(requestedCount, (int)_serverConfig.ServerMaxRequestedCountAttributeFromRequest);
                requestedCount = Math.Max(requestedCount, (int)_serverConfig.ServerMinRequestedCountAttributeFromRequest);

                (var fileEntities, var directoryEntities, var isRootFolder, var totalMatches) = await ContentExplorer.GetBrowseResultItems(objectID, startingIndex, requestedCount);

                response = new();

                var localIpEndpoint = $"{connection!.LocalIpAddress!.MapToIPv4()}:{connection!.LocalPort!}";
                var logger = _logger;

                response.Result.DidlLite.Containers = directoryEntities.Length != 0
                    ? directoryEntities
                        .AsArray()
                        .Select(directory =>
                        {
                            try
                            {
                                return directory.MapContainer(localIpEndpoint, isRootFolder);
                            }
                            catch (Exception ex)
                            {
                                logger.LogGeneralErrorMessage(ex, directory.DirectoryFullPath);
                            }
                            return null;
                        })
                        .Where(static (container) => container != null)
                        .Select(static (container) => container!)
                        .ToArray()
                    : [];

                response.Result.DidlLite.BrowseItems = fileEntities.Length != 0
                    ? fileEntities
                        .AsArray()
                        .Select(file =>
                        {
                            try
                            {
                                return file.MapItem(localIpEndpoint, isRootFolder);
                            }
                            catch (Exception ex)
                            {
                                logger.LogGeneralErrorMessage(ex, file.FilePhysicalFullPath);
                            }
                            return null;
                        })
                        .Where(static (browseItems) => browseItems != null)
                        .Select(static (browseItems) => browseItems!)
                        .ToArray()
                    : [];

                response.TotalMatches = totalMatches;
                response.NumberReturned = (uint)(response.Result.DidlLite.BrowseItems.Length + response.Result.DidlLite.Containers.Length);
                response.UpdateID = (uint)FileWatcherManager.UpdatesCount;

                InformationStartBrowseRequest(
                    connection?.RemoteIpAddress,
                    connection?.RemotePort,
                    objectID,
                    requestedCount,
                    startingIndex,
                    response.NumberReturned,
                    response.TotalMatches,
                    (DateTime.Now - startTime).TotalMilliseconds
                    );

                _ = _browseLock.Release();

                DebugBrowseRequestFinish(objectID);

                return response;
            }
            catch (Exception ex)
            {
                _ = _browseLock.Release();

                DebugBrowseRequestError(objectID);

                _logger.LogGeneralErrorMessage(ex);
                return new();
            }
        }
        public GetSearchCapabilities GetSearchCapabilities()
        {
            var connection = _httpContextAccessor.HttpContext?.Connection;
            LoggerHelper.LogDebugConnectionInformation(
                _logger,
                nameof(GetSearchCapabilities),
                connection?.RemoteIpAddress,
                connection?.RemotePort,
                connection?.LocalIpAddress,
                connection?.LocalPort,
                _httpContextAccessor.HttpContext?.Request.Path.Value,
                _httpContextAccessor.HttpContext?.Request.Method);
            _logger.LogGeneralInformationMessage(nameof(GetSearchCapabilities));

            return new() { SearchCaps = "*" };
        }
        public GetSortCapabilities GetSortCapabilities()
        {
            var connection = _httpContextAccessor.HttpContext?.Connection;
            LoggerHelper.LogDebugConnectionInformation(
                _logger,
                nameof(GetSortCapabilities),
                connection?.RemoteIpAddress,
                connection?.RemotePort,
                connection?.LocalIpAddress,
                connection?.LocalPort,
                _httpContextAccessor.HttpContext?.Request.Path.Value,
                _httpContextAccessor.HttpContext?.Request.Method);
            // not found operation in real usage
            _logger.LogGeneralWarningMessage(nameof(GetSortCapabilities));

            return new() { SortCaps = "*" };
        }

        public IsAuthorized IsAuthorized(string deviceID)
        {
            var connection = _httpContextAccessor.HttpContext?.Connection;
            LoggerHelper.LogDebugConnectionInformation(
                _logger,
                nameof(IsAuthorized),
                connection?.RemoteIpAddress,
                connection?.RemotePort,
                connection?.LocalIpAddress,
                connection?.LocalPort,
                _httpContextAccessor.HttpContext?.Request.Path.Value,
                _httpContextAccessor.HttpContext?.Request.Method);
            // not found operation in real usage
            _logger.LogGeneralWarningMessage(nameof(IsAuthorized));

            return new() { Result = 1 };
        }
        public GetSystemUpdateID GetSystemUpdateID()
        {
            var connection = _httpContextAccessor.HttpContext?.Connection;
            LoggerHelper.LogDebugConnectionInformation(
                _logger,
                nameof(GetSystemUpdateID),
                connection?.RemoteIpAddress,
                connection?.RemotePort,
                connection?.LocalIpAddress,
                connection?.LocalPort,
                _httpContextAccessor.HttpContext?.Request.Path.Value,
                _httpContextAccessor.HttpContext?.Request.Method);
            // not found operation in real usage
            _logger.LogGeneralWarningMessage(nameof(GetSystemUpdateID));

            return new() { Id = (uint)FileWatcherManager.UpdatesCount };
        }
        public X_GetFeatureList X_GetFeatureList()
        {
            var connection = _httpContextAccessor.HttpContext?.Connection;
            LoggerHelper.LogDebugConnectionInformation(
                _logger,
                nameof(X_GetFeatureList),
                connection?.RemoteIpAddress,
                connection?.RemotePort,
                connection?.LocalIpAddress,
                connection?.LocalPort,
                _httpContextAccessor.HttpContext?.Request.Path.Value,
                _httpContextAccessor.HttpContext?.Request.Method);
            // not found operation in real usage
            _logger.LogGeneralWarningMessage(nameof(X_GetFeatureList));

            return new() { FeatureList = [] };
        }

        public X_SetBookmark X_SetBookmark(int categoryType, int RID, string objectID, int posSecond)
        {
            var connection = _httpContextAccessor.HttpContext?.Connection;
            LoggerHelper.LogDebugConnectionInformation(
                _logger,
                nameof(X_SetBookmark),
                connection?.RemoteIpAddress,
                connection?.RemotePort,
                connection?.LocalIpAddress,
                connection?.LocalPort,
                _httpContextAccessor.HttpContext?.Request.Path.Value,
                _httpContextAccessor.HttpContext?.Request.Method);
            // not found operation in real usage
            WarningX_SetBookmarkRequestInfo(
                nameof(X_SetBookmark),
                categoryType,
                RID,
                objectID,
                posSecond
            );

            return new() { };
        }
    }
}
