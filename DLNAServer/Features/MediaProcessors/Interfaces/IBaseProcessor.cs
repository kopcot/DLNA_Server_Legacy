using DLNAServer.Database.Entities;
using DLNAServer.Helpers.Interfaces;

namespace DLNAServer.Features.MediaProcessors.Interfaces
{
    public interface IBaseProcessor : ITerminateAble, IInitializeAble
    {
        Task<bool> FillEmptyInfoAsync(IEnumerable<FileEntity> fileEntities, bool setCheckedForFailed = true);
        Task<bool> FillEmptyMetadataAsync(IEnumerable<FileEntity> fileEntities, bool setCheckedForFailed = true);
        Task<bool> FillEmptyThumbnailsAsync(IEnumerable<FileEntity> fileEntities, bool setCheckedForFailed = true);
    }
}
