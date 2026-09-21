namespace DLNAServer.Features.FileWatcher
{
    public partial class FileWatcherService
    {
        [LoggerMessage(1, LogLevel.Information, "Starting File Watcher Service...")]
        partial void InformationStarting();
        [LoggerMessage(2, LogLevel.Debug, "Started handling event '{changeType}' for '{fullPath}'\nEventID: {guid}")]
        partial void DebugEventStarted(WatcherChangeTypes changeType, string fullPath, Guid guid);
        [LoggerMessage(3, LogLevel.Debug, "Done handling event '{changeType}' for '{fullPath}'\nEventID: {guid}")]
        partial void DebugEventDone(WatcherChangeTypes changeType, string fullPath, Guid guid);
        [LoggerMessage(4, LogLevel.Debug, "Event '{changeType}' filtered out by thumbnail subfolder for '{fullPath}'")]
        partial void DebugEventFilteredForThumbnailSubfolder(WatcherChangeTypes changeType, string fullPath);
        [LoggerMessage(5, LogLevel.Debug, "Event '{changeType}' filtered out by extension or by not a directory for '{fullPath}'")]
        partial void DebugEventFilteredForExtensionOrNotDirectory(WatcherChangeTypes changeType, string fullPath);
        [LoggerMessage(6, LogLevel.Warning, "Unable to dequeue file event. File events count: {fileEventCount}")]
        partial void WarningUnableToDequeueEvent(int fileEventCount);
        [LoggerMessage(7, LogLevel.Debug, "Actual '{changeType}' event in progress for '{fullPath}'\nTotal raised event in queue: {fileEventCount}")]
        partial void DebugActualRaisedEvent(string fullPath, WatcherChangeTypes changeType, int fileEventCount);
        [LoggerMessage(8, LogLevel.Information, "Finished all active file raised events. Job Id: '{guid}'. Waiting for a next.")]
        partial void InformationFinishedActiveRaisedEvents(Guid guid);
        [LoggerMessage(9, LogLevel.Information, "Started file raised events. Job Id: '{guid}'.")]
        partial void InformationStartedCheckingRaisedEvents(Guid guid);
        [LoggerMessage(10, LogLevel.Information, "Finished checking file raised events.")]
        partial void InformationFinishedCheckingRaisedEvents();
    }
}
