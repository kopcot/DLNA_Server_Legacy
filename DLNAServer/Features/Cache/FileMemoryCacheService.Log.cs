namespace DLNAServer.Features.Cache
{
    public partial class FileMemoryCacheService
    {
        [LoggerMessage(1, LogLevel.Information, "Starting File Memory Cache Service...")]
        partial void InformationStarting();
        [LoggerMessage(2, LogLevel.Information, "Finished checking file raised events for background file caching.")]
        partial void InformationFinishedCheckingRaisedEvents();
        [LoggerMessage(3, LogLevel.Information, "Started file caching raised events. Job Id: '{guid}'.")]
        partial void InformationStartedCheckingRaisedEvents(Guid guid);
        [LoggerMessage(4, LogLevel.Information, "Finished all active file caching raised events. Job Id: '{guid}'. Waiting for a next.")]
        partial void InformationFinishedActiveRaisedEvents(Guid guid);
        [LoggerMessage(5, LogLevel.Debug, "Actual caching event in progress for '{guid}'\nTotal raised caching event in queue: {fileCachingEventCount}")]
        partial void DebugActualRaisedEvent(Guid guid, int fileCachingEventCount);
        [LoggerMessage(6, LogLevel.Warning, "Missing file entity with ID {guid}")]
        partial void WarningMissingFileEntityWithID(Guid guid);
    }
}
