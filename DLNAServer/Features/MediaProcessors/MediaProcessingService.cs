using DLNAServer.Database.Entities;
using DLNAServer.Features.MediaProcessors.Interfaces;
using DLNAServer.Helpers.Logger;
using DLNAServer.Types.DLNA;

namespace DLNAServer.Features.MediaProcessors
{
    public sealed class MediaProcessingService : IMediaProcessingService
    {
        private readonly IAudioProcessor AudioProcessor;
        private readonly IImageProcessor ImageProcessor;
        private readonly IVideoProcessor VideoProcessor;
        private readonly ILogger<MediaProcessingService> Logger;

        public MediaProcessingService(
            IAudioProcessor audioProcessor,
            IImageProcessor imageProcessor,
            IVideoProcessor videoProcessor,
            ILogger<MediaProcessingService> logger)
        {
            AudioProcessor = audioProcessor;
            ImageProcessor = imageProcessor;
            VideoProcessor = videoProcessor;
            Logger = logger;
        }

        public async Task<bool> FillEmptyInfoAsync(IEnumerable<FileEntity> fileEntities, bool setCheckedForFailed = true)
        {
            bool changed = false;

            try
            {
                if (!fileEntities.Any())
                {
                    return false;
                }

                var mediaGroup = fileEntities
                    .GroupBy(static (fe) => fe.FileDlnaMime.ToDlnaMedia())
                    .ToDictionary(static (g) => g.Key, static (g) => g.ToArray());

                foreach (var group in mediaGroup)
                {
                    switch (group.Key)
                    {
                        case DlnaMedia.Audio:
                            changed |= await AudioProcessor.FillEmptyInfoAsync(group.Value, setCheckedForFailed);
                            break;
                        case DlnaMedia.Image:
                            changed |= await ImageProcessor.FillEmptyInfoAsync(group.Value, setCheckedForFailed);
                            break;
                        case DlnaMedia.Video:
                            changed |= await VideoProcessor.FillEmptyInfoAsync(group.Value, setCheckedForFailed);
                            break;
                        default:
                            throw new NotSupportedException($"Unsupported media type: {group.Key}");
                    }
                }
                return changed;
            }
            catch (Exception ex)
            {
                Logger.LogGeneralErrorMessage(ex);
            }
            return false;
        }

        public async Task<bool> FillEmptyMetadataAsync(IEnumerable<FileEntity> fileEntities, bool setCheckedForFailed = true)
        {
            bool changed = false;

            try
            {
                if (!fileEntities.Any())
                {
                    return false;
                }

                var mediaGroup = fileEntities
                    .GroupBy(static (fe) => fe.FileDlnaMime.ToDlnaMedia())
                    .ToDictionary(static (g) => g.Key, static (g) => g.ToArray());

                foreach (var group in mediaGroup)
                {
                    switch (group.Key)
                    {
                        case DlnaMedia.Audio:
                            changed |= await AudioProcessor.FillEmptyMetadataAsync(group.Value, setCheckedForFailed);
                            break;
                        case DlnaMedia.Image:
                            changed |= await ImageProcessor.FillEmptyMetadataAsync(group.Value, setCheckedForFailed);
                            break;
                        case DlnaMedia.Video:
                            changed |= await VideoProcessor.FillEmptyMetadataAsync(group.Value, setCheckedForFailed);
                            break;
                        default:
                            throw new NotSupportedException($"Unsupported media type: {group.Key}");
                    }
                }
                return changed;
            }
            catch (Exception ex)
            {
                Logger.LogGeneralErrorMessage(ex);
            }
            return false;
        }
        public async Task<bool> FillEmptyThumbnailsAsync(IEnumerable<FileEntity> fileEntities, bool setCheckedForFailed = true)
        {
            bool changed = false;

            try
            {
                if (!fileEntities.Any())
                {
                    return false;
                }

                var mediaGroup = fileEntities
                    .GroupBy(static (fe) => fe.FileDlnaMime.ToDlnaMedia())
                    .ToDictionary(static (g) => g.Key, static (g) => g.ToArray());

                foreach (var group in mediaGroup)
                {
                    switch (group.Key)
                    {
                        case DlnaMedia.Audio:
                            changed |= await AudioProcessor.FillEmptyThumbnailsAsync(group.Value, setCheckedForFailed);
                            break;
                        case DlnaMedia.Image:
                            changed |= await ImageProcessor.FillEmptyThumbnailsAsync(group.Value, setCheckedForFailed);
                            break;
                        case DlnaMedia.Video:
                            changed |= await VideoProcessor.FillEmptyThumbnailsAsync(group.Value, setCheckedForFailed);
                            break;
                        default:
                            throw new NotSupportedException($"Unsupported media type: {group.Key}");
                    }
                }
                return changed;
            }
            catch (Exception ex)
            {
                Logger.LogGeneralErrorMessage(ex);
            }
            return false;
        }
        public Task InitializeAsync()
        {
            throw new NotImplementedException();
        }
        public Task TerminateAsync()
        {
            throw new NotImplementedException();
        }
    }
}
