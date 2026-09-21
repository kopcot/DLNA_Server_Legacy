using DLNAServer.Common;
using DLNAServer.Features.PhysicalFile.Interfaces;
using DLNAServer.Helpers.Logger;

namespace DLNAServer.Features.PhysicalFile
{
    public partial class FileService : IFileService
    {
        private readonly ILogger<FileService> _logger;
        public FileService(
            ILogger<FileService> logger)
        {
            _logger = logger;
        }
        public async Task<ReadOnlyMemory<byte>?> ReadFileAsync(string filePath, long maxSizeOfFile = long.MaxValue, CancellationToken cancellationToken = default)
        {
            //const int bufferSize = 64 * 1_024; // less as 85,000 bytes in size for not need to use Large Object Heap (LOH) 
            try
            {
                FileInfo fileInfo = new(filePath);
                if (!fileInfo.Exists)
                {
                    return null;
                }
                LogCheckFileSize();
                if (fileInfo.Length > int.MaxValue ||
                    fileInfo.Length > maxSizeOfFile ||
                    fileInfo.Length == 0)
                {
                    LogFileSizeIncorrect(
                        fileInfo.Length,
                        int.MaxValue,
                        maxSizeOfFile,
                        filePath
                    );
                    return null;
                }
                
                using (CancellationTokenSource timeoutCts = new(TimeSpanValues.TimeMin10))
                using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                {
                    // versions with MemoryMappedFile, StreamReader unnecessary,
                    // no any real benefit , as it is return whole byte[]
                    try
                    {
                        return await File.ReadAllBytesAsync(filePath, linkedCts.Token);
                    }
                    catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested)
                    {
                        _logger.LogWarning($"File read timeout. {ex.Message}");
                        return null;
                    }
                    catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogDebug($"Client disconnected. {ex.Message}");
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                return null;
            }
        }
    }
}
