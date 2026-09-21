using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;
using DLNAServer.Common;
using DLNAServer.Configuration;
using DLNAServer.Database.Entities;
using DLNAServer.Database.Repositories.Interfaces;
using DLNAServer.Features.MediaProcessors.Interfaces;
using DLNAServer.Helpers.Logger;
using DLNAServer.Types.DLNA;
using System.Buffers;
using Xabe.FFmpeg;

namespace DLNAServer.Features.MediaProcessors
{
    public partial class AudioProcessor : IAudioProcessor
    {
        private readonly ILogger<AudioProcessor> _logger;
        private readonly ServerConfig _serverConfig;
        private readonly IFileRepository FileRepository;
        private readonly IFFmpegService FFmpegService;
        public AudioProcessor(
            ILogger<AudioProcessor> logger,
            ServerConfig serverConfig,
            IFileRepository fileRepository,
            IFFmpegService mpegService)
        {
            _logger = logger;
            _serverConfig = serverConfig;
            FileRepository = fileRepository;
            FFmpegService = mpegService;
        }

        public Task InitializeAsync()
        {
            return FFmpegService.EnsureFFmpegDownloaded();
        }
        public async Task<bool> FillEmptyInfoAsync(IEnumerable<FileEntity> fileEntities, bool setCheckedForFailed = true)
        {
            int estimated = fileEntities.TryGetNonEnumeratedCount(out int c) ? c
                : fileEntities.Count();

            using ArrayPoolBufferWriter<FileEntity> writer = new(estimated);
            //ArrayBufferWriter<FileEntity> writer = new(estimated);

            foreach (var fe in fileEntities)
            {
                if (fe.FileDlnaMime.ToDlnaMedia() == DlnaMedia.Audio
                    && (!fe.IsMetadataChecked || !fe.IsThumbnailChecked))
                {
                    writer.Write(fe);
                }
            }

            if (writer.WrittenCount > 0)
            {
                await RefreshInfoAsync(writer.WrittenMemory, setCheckedForFailed);

                writer.Clear();
                return true;
            }

            writer.Clear();
            return false;
        }
        private async Task RefreshInfoAsync(ReadOnlyMemory<FileEntity> fileEntities, bool setCheckedForFailed = true)
        {
            try
            {
                if (fileEntities.Length == 0)
                {
                    return;
                }

                await FFmpegService.EnsureFFmpegDownloaded();

                if (fileEntities.Length == 1)
                {
                    var file = fileEntities.Span[0];
                    if (!file.IsMetadataChecked && _serverConfig.GenerateMetadataForLocalAudio)
                    {
                        await RefreshSingleFileMetadataAsync(file, setCheckedForFailed);
                    }

                    file.IsThumbnailChecked = true;
                }
                else
                {
                    var maxDegreeOfParallelism = Math.Max(Math.Min(fileEntities.Length, (int)_serverConfig.ServerMaxDegreeOfParallelism), 1);

                    await Parallel.ForAsync(
                        0,
                        fileEntities.Length,
                        parallelOptions: new() { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                        async (index, _) =>
                        {
                            var file = fileEntities.Span[index];

                            if (!file.IsMetadataChecked && _serverConfig.GenerateMetadataForLocalAudio)
                            {
                                await RefreshSingleFileMetadataAsync(file, setCheckedForFailed);
                            }

                            file.IsThumbnailChecked = true;
                        });
                }

                _ = await FileRepository.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }
        public async Task<bool> FillEmptyMetadataAsync(IEnumerable<FileEntity> fileEntities, bool setCheckedForFailed = true)
        {
            int estimated = fileEntities.TryGetNonEnumeratedCount(out int c) ? c
                : fileEntities.Count();

            using ArrayPoolBufferWriter<FileEntity> writer = new(estimated);
            //ArrayBufferWriter<FileEntity> writer = new(estimated);

            foreach (var fe in fileEntities)
            {
                if (fe.FileDlnaMime.ToDlnaMedia() == DlnaMedia.Audio
                    && !fe.IsMetadataChecked)
                {
                    writer.Write(fe);
                }
            }

            if (writer.WrittenCount > 0)
            {
                await RefreshMetadataAsync(writer.WrittenMemory, setCheckedForFailed);

                writer.Clear();
                return true;
            }

            writer.Clear();
            return false;
        }
        private async Task RefreshMetadataAsync(ReadOnlyMemory<FileEntity> fileEntities, bool setCheckedForFailed = true)
        {
            try
            {
                if (!_serverConfig.GenerateMetadataForLocalAudio
                    || fileEntities.Length == 0)
                {
                    return;
                }

                await FFmpegService.EnsureFFmpegDownloaded();

                if (fileEntities.Length == 1)
                {
                    var file = fileEntities.Span[0];
                    await RefreshSingleFileMetadataAsync(file, setCheckedForFailed);
                }
                else
                {
                    var maxDegreeOfParallelism = Math.Max(Math.Min(fileEntities.Length, (int)_serverConfig.ServerMaxDegreeOfParallelism), 1);

                    await Parallel.ForAsync(
                        0,
                        fileEntities.Length,
                        parallelOptions: new() { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                        async (index, _) =>
                        {
                            var file = fileEntities.Span[index];

                            await RefreshSingleFileMetadataAsync(file, setCheckedForFailed);
                        });
                }

                _ = await FileRepository.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }

        private async Task RefreshSingleFileMetadataAsync(FileEntity file, bool setCheckedForFailed)
        {
            var audioMetadata = await GetFileMetadataAsync(file);
            file.AudioMetadata = audioMetadata;
            if (audioMetadata != null)
            {
                file.IsMetadataChecked = true;

                InformationSetMetadata(file.FilePhysicalFullPath);
            }
            if (setCheckedForFailed &&
                audioMetadata == null &&
                (DateTime.Now - file.CreatedInDB) > TimeSpanValues.TimeHours12)
            {
                file.IsMetadataChecked = true;

                WarningSetMetadataFailed(file.FilePhysicalFullPath);
            }
        }
        private async Task<MediaAudioEntity?> GetFileMetadataAsync(FileEntity fileEntity)
        {
            if (fileEntity == null)
            {
                return null;
            }

            FileInfo fileInfo = new(fileEntity.FilePhysicalFullPath);
            if (!fileInfo.Exists || fileInfo.Length == 0)
            {
                return null;
            }

            try
            {
                using (CancellationTokenSource cancellationTokenSource = new(TimeSpanValues.TimeMin5))
                {
                    if (await FFmpegService.TryGetMediaInfo(fileEntity.FilePhysicalFullPath, cancellationTokenSource.Token) is IMediaInfo mediaInfo)
                    {
                        fileEntity.FileSizeInBytes = mediaInfo.Size;
                        return ExtractAudioMetadata(ref mediaInfo);
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                return null;
            }
        }
        private MediaAudioEntity? ExtractAudioMetadata(ref IMediaInfo mediaInfo)
        {
            try
            {
                if (mediaInfo.AudioStreams.FirstOrDefault() is IAudioStream audioStream)
                {
                    return new()
                    {
                        FilePhysicalFullPath = mediaInfo.Path,
                        Duration = audioStream.Duration,
                        Codec = !string.IsNullOrWhiteSpace(audioStream.Codec)
                            ? audioStream.Codec
                            : null,
                        Bitrate = audioStream.Bitrate,
                        Channels = audioStream.Channels,
                        Language = !string.IsNullOrWhiteSpace(audioStream.Language)
                            ? audioStream.Language
                            : null,
                        SampleRate = audioStream.SampleRate
                    };
                }
                else
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                return null;
            }
        }
        public async Task<bool> FillEmptyThumbnailsAsync(IEnumerable<FileEntity> fileEntities, bool setCheckedForFailed = true)
        {
            int estimated = fileEntities.TryGetNonEnumeratedCount(out int c) ? c
                : fileEntities.Count();

            using ArrayPoolBufferWriter<FileEntity> writer = new(estimated);
            //ArrayBufferWriter<FileEntity> writer = new(estimated);

            foreach (var fe in fileEntities)
            {
                if (fe.FileDlnaMime.ToDlnaMedia() == DlnaMedia.Audio
                    && !fe.IsThumbnailChecked)
                {
                    writer.Write(fe);
                }
            }

            if (writer.WrittenCount > 0)
            {
                await RefreshThumbnailsAsync(writer.WrittenMemory);

                writer.Clear();
                return true;
            }

            writer.Clear();
            return false;
        }
        private async Task RefreshThumbnailsAsync(ReadOnlyMemory<FileEntity> fileEntities)
        {
            try
            {
                if (fileEntities.Length == 0)
                {
                    return;
                }

                if (fileEntities.Length == 1)
                {
                    var file = fileEntities.Span[0];
                    file.IsThumbnailChecked = true;
                }
                else
                {
                    var maxDegreeOfParallelism = Math.Max(Math.Min(fileEntities.Length, (int)_serverConfig.ServerMaxDegreeOfParallelism), 1);

                    _ = Parallel.For(
                        0,
                        fileEntities.Length,
                        parallelOptions: new() { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                        (index) =>
                        {
                            var file = fileEntities.Span[index];

                            file.IsThumbnailChecked = true;
                        });
                }

                _ = await FileRepository.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }

        public Task TerminateAsync()
        {
            return Task.CompletedTask;
        }
    }
}
