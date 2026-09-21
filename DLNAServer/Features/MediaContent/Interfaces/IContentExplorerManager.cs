using DLNAServer.Database.Entities;
using DLNAServer.Helpers.Interfaces;
using DLNAServer.Types.DLNA;

namespace DLNAServer.Features.MediaContent.Interfaces
{
    public interface IContentExplorerManager : IInitializeAble, ITerminateAble
    {
        Task RefreshFoundFilesAsync(Dictionary<DlnaMime, ReadOnlyMemory<string>> inputFiles, bool shouldBeAdded);
        Task<List<DirectoryEntity>> GetNewDirectoryEntities(IEnumerable<string?> folders);
        Task<(ReadOnlyMemory<FileEntity> fileEntities, ReadOnlyMemory<DirectoryEntity> directoryEntities, bool isRootFolder, uint totalMatches)> GetBrowseResultItems(
            string objectID,
            int startingIndex,
            int requestedCount
            );
        Task<ReadOnlyMemory<FileEntity>> CheckFilesExistingAsync(ReadOnlyMemory<FileEntity> fileEntities);
        Task<ReadOnlyMemory<DirectoryEntity>> CheckDirectoriesExistingAsync(ReadOnlyMemory<DirectoryEntity> directoryEntities);
        Task CheckAllDirectoriesExistingAsync(int batchSize = 500);
        Task CheckAllFilesExistingAsync(int batchSize = 500);
        Task ClearThumbnailsAsync(ReadOnlyMemory<FileEntity> files, bool deleteThumbnailFile = true);
        Task ClearAllThumbnailsAsync(bool deleteThumbnailFile = true);
        Task ClearAllMetadataAsync();
        Task ClearMetadataAsync(ReadOnlyMemory<FileEntity> files);
    }
}
