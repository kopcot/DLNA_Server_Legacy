namespace DLNAServer.Features.MediaProcessors
{
    public partial class FFmpegService
    {
        [LoggerMessage(1, LogLevel.Error, "{message}")]
        partial void LogErrorFFmpegGetMediaInfo(string message);
        [LoggerMessage(2, LogLevel.Information, "Downloading the latest version of the FFMpeg to the '{executablesPath}'")]
        partial void InformationDownloadingLatestVersion(string executablesPath);
        [LoggerMessage(3, LogLevel.Information, "Finished downloading ...")]
        partial void InformationDownloadingLatestVersionFinished();
    }
}
