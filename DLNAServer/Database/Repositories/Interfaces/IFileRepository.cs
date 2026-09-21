using DLNAServer.Database.Entities;

namespace DLNAServer.Database.Repositories.Interfaces
{
    public interface IFileRepository : IBaseRepository<FileEntity>
    {
        Task<ReadOnlyMemory<FileEntity>> GetAllByAddedToDbAsync(int takeNumber, IEnumerable<string> excludeFolders, bool useCachedResult = true);
        Task<ReadOnlyMemory<FileEntity>> GetAllByParentDirectoryIdsAsync(IEnumerable<Guid> expectedDirectories, IEnumerable<string> excludeFolders, bool useCachedResult = true);
        Task<ReadOnlyMemory<FileEntity>> GetAllByParentDirectoryIdsAsync(IEnumerable<string> expectedDirectories, IEnumerable<string> excludeFolders, bool useCachedResult = true);
        Task<ReadOnlyMemory<FileEntity>> GetAllWithEmptyParentDirectoryIdsAsync(string pathFullName, IEnumerable<string> excludeFolders, bool useCachedResult = true);
        Task<ReadOnlyMemory<string>> GetAllFileFullNamesAsync(string? filterExtension = null, bool useCachedResult = true);
        Task<(bool ok, int? minDepth)> GetMinimalDepthAsync(bool useCachedResult = true);
        Task<ReadOnlyMemory<FileEntity>> GetAllByDirectoryDepthAsync(int depth, bool useCachedResult = true);
        Task<ReadOnlyMemory<FileEntity>> GetAllByDirectoryDepthAsync(int depth, int skip, int take, bool useCachedResult = true);
        Task<ReadOnlyMemory<FileEntity>> GetAllByPathFullNameAsync(string pathFullName, bool useCachedResult = true);
        /// <summary>
        /// Retrieves all existing file full path names that match the specified <paramref name="pathFullNames"/> collection as <see cref="FileEntity.FilePhysicalFullPath"/>
        /// </summary>
        Task<ReadOnlyMemory<string>> GetAllExistingByPathFullNamesAsync(IEnumerable<string> pathFullNames, bool useCachedResult = true);
    }
}
