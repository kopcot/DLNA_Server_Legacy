using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;
using DLNAServer.Common;
using DLNAServer.Configuration;
using DLNAServer.Database.Entities;
using DLNAServer.Database.Repositories.Interfaces;
using DLNAServer.Features.MediaContent.Interfaces;
using DLNAServer.Features.MediaProcessors.Interfaces;
using DLNAServer.Helpers.Database;
using DLNAServer.Helpers.Files;
using DLNAServer.Helpers.Logger;
using DLNAServer.Types.DLNA;
using Microsoft.EntityFrameworkCore;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Runtime.InteropServices;

namespace DLNAServer.Features.MediaContent
{
    public partial class ContentExplorerManager : IContentExplorerManager
    {
        private readonly ILogger<ContentExplorerManager> _logger;
        private readonly ServerConfig _serverConfig;

        private readonly Lazy<IFileRepository> _fileRepositoryLazy;
        private readonly Lazy<IDirectoryRepository> _directoryRepositoryLazy;
        private readonly Lazy<IThumbnailDataRepository> _thumbnailDataRepositoryLazy;
        private readonly Lazy<IMediaProcessingService> _mediaProcessingServiceLazy;
        private readonly Lazy<IAudioMetadataRepository> _audioMetadataRepositoryLazy;
        private readonly Lazy<IVideoMetadataRepository> _videoMetadataRepositoryLazy;
        private readonly Lazy<ISubtitleMetadataRepository> _subtitleMetadataRepositoryLazy;
        private readonly Lazy<IThumbnailRepository> _thumbnailRepositoryLazy;

        private IFileRepository FileRepository => _fileRepositoryLazy.Value;
        private IDirectoryRepository DirectoryRepository => _directoryRepositoryLazy.Value;
        private IThumbnailDataRepository ThumbnailDataRepository => _thumbnailDataRepositoryLazy.Value;
        private IMediaProcessingService MediaProcessingService => _mediaProcessingServiceLazy.Value;
        private IAudioMetadataRepository AudioMetadataRepository => _audioMetadataRepositoryLazy.Value;
        private IVideoMetadataRepository VideoMetadataRepository => _videoMetadataRepositoryLazy.Value;
        private ISubtitleMetadataRepository SubtitleMetadataRepository => _subtitleMetadataRepositoryLazy.Value;
        private IThumbnailRepository ThumbnailRepository => _thumbnailRepositoryLazy.Value;
        private readonly ArrayPool<DirectoryEntity> poolDirectoryEntity = ArrayPool<DirectoryEntity>.Shared;
        public ContentExplorerManager(
            ILogger<ContentExplorerManager> logger,
            ServerConfig serverConfig,
            Lazy<IFileRepository> fileRepositoryLazy,
            Lazy<IDirectoryRepository> directoryRepositoryLazy,
            Lazy<IThumbnailDataRepository> thumbnailDataRepositoryLazy,
            Lazy<IMediaProcessingService> mediaProcessingServiceLazy,
            Lazy<IAudioMetadataRepository> audioMetadataRepositoryLazy,
            Lazy<IVideoMetadataRepository> videoMetadataRepositoryLazy,
            Lazy<ISubtitleMetadataRepository> subtitleMetadataRepositoryLazy,
            Lazy<IThumbnailRepository> thumbnailRepositoryLazy)
        {
            _logger = logger;
            _serverConfig = serverConfig;
            _fileRepositoryLazy = fileRepositoryLazy;
            _directoryRepositoryLazy = directoryRepositoryLazy;
            _thumbnailDataRepositoryLazy = thumbnailDataRepositoryLazy;
            _mediaProcessingServiceLazy = mediaProcessingServiceLazy;
            _audioMetadataRepositoryLazy = audioMetadataRepositoryLazy;
            _videoMetadataRepositoryLazy = videoMetadataRepositoryLazy;
            _subtitleMetadataRepositoryLazy = subtitleMetadataRepositoryLazy;
            _thumbnailRepositoryLazy = thumbnailRepositoryLazy;
        }

        public async Task InitializeAsync()
        {
            var inputFiles = GetAllFilesInFolders(_serverConfig.SourceFolders, true);
            await RefreshFoundFilesAsync(inputFiles, shouldBeAdded: true);
            foreach (var sourceFolder in _serverConfig.SourceFolders)
            {
                await CheckParentDirectoriesAsync(sourceFolder);
            }

            await CheckAllFilesExistingAsync();
            await CheckAllDirectoriesExistingAsync();

            var filesInDbCount = await FileRepository.GetCountAsync();
            var directoriesInDbCount = await DirectoryRepository.GetCountAsync();
            InformationRefreshedInfo((int)directoriesInDbCount, (int)filesInDbCount);
        }
        public Task TerminateAsync()
        {
            return Task.CompletedTask;
        }
        private Dictionary<DlnaMime, ReadOnlyMemory<string>> GetAllFilesInFolders(List<string> sourceFolders, bool withSubdirectories)
        {
            Dictionary<DlnaMime, HashSet<string>> filesInSourceFolders = [];

            HashSet<string> excludeFolders = new(_serverConfig.ExcludeFolders, StringComparer.OrdinalIgnoreCase);
            var mediaFileExtensions = _serverConfig.MediaFileExtensions
                .ToDictionary(
                    keySelector: static (kvp) => kvp.Key,
                    elementSelector: static (kvp) => kvp.Value.Key,
                    comparer: StringComparer.OrdinalIgnoreCase);

            DirectoryInfo directory;
            var enumOptions = enumerationOptionsDefault;
            FileSystemEnumerable<(string pathFullName, DlnaMime mime)> enumerateFiles;

            foreach (var sourceFolder in sourceFolders)
            {
                directory = new(sourceFolder);
                if (!directory.Exists)
                {
                    WarningDirectoryNotExists(sourceFolder);
                    continue;
                }

                enumOptions.ReturnSpecialDirectories = withSubdirectories;

                enumerateFiles = new
                    (
                        directory: directory.FullName,
                        transform: (ref FileSystemEntry entry) =>
                        {
                            string fileFullPath = entry.ToFullPath();
                            var extension = Path.GetExtension(fileFullPath);
                            mediaFileExtensions.TryGetValue(extension, out var mime);

                            return (fileFullPath, mime);
                        },
                        options: enumOptions
                    )
                {
                    ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                    {
                        string fileFullPath = entry.ToFullPath();
                        var extension = Path.GetExtension(fileFullPath);
                        return mediaFileExtensions.TryGetValue(extension, out DlnaMime mime)
                            && mime != DlnaMime.Undefined
                            && !excludeFolders.Any(skip => fileFullPath.Contains(skip));
                    }
                };

                foreach (var (fileFullPath, mime) in enumerateFiles)
                {
                    ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(filesInSourceFolders, mime, out _);

                    list ??= [];
                    list.Add(fileFullPath);
                }
            }

            return filesInSourceFolders
                .ToDictionary(
                    keySelector: static (g) => g.Key,
                    elementSelector: static (g) => new ReadOnlyMemory<string>(g.Value.ToArray()));
        }

        private static readonly EnumerationOptions enumerationOptionsDefault = new()
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.Hidden
                             | FileAttributes.System
                             | FileAttributes.Temporary
                             | FileAttributes.SparseFile
                             | FileAttributes.ReparsePoint
                             | FileAttributes.Compressed
                             ,
            //BufferSize = 1_024_000,
            IgnoreInaccessible = false,
            // unable to use search patters from ServerConfig.Extensions,
            // as for Linux it is different between .jpg, .JPG, .Jpg
            // 'MatchCasing = MatchCasing.CaseInsensitive' is not helpful 
            MatchCasing = MatchCasing.CaseInsensitive,
            MatchType = MatchType.Simple,
            MaxRecursionDepth = int.MaxValue,
            ReturnSpecialDirectories = true,
        };

        private static readonly SemaphoreSlim semaphoreRefreshFoundFiles = new(1, 1);
        /// <summary>
        /// Scans the provided grouped file list by DLNA MIME type, detects new files not present in the database,
        /// creates corresponding <see cref="FileEntity"/> and <see cref="DirectoryEntity"/> entries,<br/>
        /// and updates both the database and cached file/directory lists.
        /// Ensures thread safety via semaphores and optimizes file checks with controlled parallelism.
        /// </summary>
        /// <param name="inputFiles">Files to check and add to database</param>
        /// <param name="shouldBeAdded"><see langword="true"/> if <paramref name="inputFiles"/> should not exists in the database</param>
        /// <returns></returns>c
        public async Task RefreshFoundFilesAsync(Dictionary<DlnaMime, ReadOnlyMemory<string>> inputFiles, bool shouldBeAdded)
        {
            try
            {
                _ = await semaphoreRefreshFoundFiles.WaitAsync(TimeSpanValues.TimeMin5);

                List<FileEntity> fileEntities = [];

                using (SemaphoreSlim semaphoreGetFilesFromDB = new(1, 1))
                using (SemaphoreSlim semaphoreAddRange = new(1, 1))
                {
                    await Parallel.ForEachAsync(
                        source: inputFiles,
                        parallelOptions: new ParallelOptions
                        {
                            MaxDegreeOfParallelism = (int)_serverConfig.ServerMaxDegreeOfParallelism,
                            CancellationToken = default
                        },
                        body: async (mimeGroup, cancellationToken) =>
                        {
                            var fileExtensionConfiguration = _serverConfig.MediaFileExtensions.FirstOrDefault(e => e.Value.Key == mimeGroup.Key);
                            var fileExtension = string.Intern(fileExtensionConfiguration.Key.ToUpperInvariant());
                            var fileDlnaProfileName = fileExtensionConfiguration.Value.Value != null
                                ? string.Intern(fileExtensionConfiguration.Value.Value)
                                : mimeGroup.Key.ToMainProfileNameString();
                            var upnpClass = mimeGroup.Key.ToDefaultDlnaItemClass();

                            _ = await semaphoreGetFilesFromDB.WaitAsync(TimeSpanValues.TimeMin5, cancellationToken);

                            var existingFiles = (await FileRepository.GetAllFileFullNamesAsync(
                                filterExtension: fileExtension,
                                useCachedResult: !shouldBeAdded))
                                .AsArray();
                            var existingFilesHash = new HashSet<string>(existingFiles, StringComparer.OrdinalIgnoreCase);

                            semaphoreGetFilesFromDB.Release();

                            var filePaths = mimeGroup.Value.AsArray();

                            var fileInfos = new HashSet<FileInfo>(filePaths.Length); // Pre-size to avoid rehash growth
                            for (int i = 0; i < filePaths.Length; i++)
                            {
                                if (!existingFilesHash.Contains(filePaths[i]))
                                {
                                    fileInfos.Add(new(filePaths[i]));
                                }
                            }

                            var maxDegreeOfParallelism = Math.Max(Math.Min(mimeGroup.Value.Length, (int)_serverConfig.ServerMaxDegreeOfParallelism), 1);

                            var addedFiles = Partitioner.Create(fileInfos)
                                .AsParallel()
                                .WithCancellation(cancellationToken)
                                .WithDegreeOfParallelism(maxDegreeOfParallelism)
                                .WithMergeOptions(ParallelMergeOptions.AutoBuffered)
                                .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
                                .Where<FileInfo>(static (fileInfo) => fileInfo.Exists)
                                .Select<FileInfo, FileEntity>(fileInfo =>
                                {
                                    return new()
                                    {
                                        CreatedInDB = _serverConfig.UseFileCreationDateTimeAsCreatedInDatabase ? fileInfo.CreationTime : DateTime.Now,
                                        FileCreateDate = fileInfo.CreationTime,
                                        FileModifiedDate = fileInfo.LastWriteTime,
                                        FileName = fileInfo.Name,
                                        FileExtension = fileExtension,
                                        Folder = fileInfo.DirectoryName,
                                        FilePhysicalFullPath = fileInfo.FullName,
                                        Title = fileInfo.Name,
                                        FileSizeInBytes = fileInfo.Length,
                                        FileDlnaMime = mimeGroup.Key,
                                        FileDlnaProfileName = fileDlnaProfileName,
                                        UpnpClass = upnpClass,
                                    };
                                })
                                .ToList();

                            await semaphoreAddRange.WaitAsync(TimeSpanValues.TimeMin5, cancellationToken);
                            fileEntities.AddRange(addedFiles);
                            semaphoreAddRange.Release();
                        });
                }

                if (fileEntities.Count == 0)
                {
                    return;
                }

                var folders = new HashSet<string>(fileEntities
                    .Where(static (f) => !string.IsNullOrWhiteSpace(f.Folder))
                    .Select(static (f) => f.Folder!),
                    StringComparer.OrdinalIgnoreCase);

                List<DirectoryEntity> directoryEntities = await GetNewDirectoryEntities(folders);
                // Fill parent directory after creation of all directories
                await FillParentDirectoriesAsync(fileEntities, directoryEntities);

                if (directoryEntities.Count != 0 || fileEntities.Count != 0)
                {

                    const int maxShownCount = 10;

                    var existingDirectories = await DirectoryRepository
                        .GetAllExistingByPathFullNamesAsync(directoryEntities.Select(static (de) => de.DirectoryFullPath), false);
                    if (existingDirectories.Length > 0)
                    {
                        WarningExistingDirectoriesInDatabase(string.Join(Environment.NewLine, existingDirectories.AsArray()));
                        directoryEntities = directoryEntities
                            .Where(de => !existingDirectories.Span.Contains(de.DirectoryFullPath))
                            .ToList();
                    }

                    var existingFiles = await FileRepository
                        .GetAllExistingByPathFullNamesAsync(fileEntities.Select(static (de) => de.FilePhysicalFullPath), false);
                    if (existingFiles.Length > 0)
                    {
                        WarningExistingFilesInDatabase(string.Join(Environment.NewLine, existingFiles.AsArray()));
                        fileEntities = fileEntities
                            .Where(fe => !existingFiles.Span.Contains(fe.FilePhysicalFullPath))
                            .ToList();
                    }
                     
                    InformationTotalAdding(directoryEntities.Count, fileEntities.Count);

                    if (directoryEntities.Count != 0)
                    {

                        InformationDirectoriesCount(
                            string.Join(Environment.NewLine, directoryEntities.Select(static (de) => de.DirectoryFullPath).Take(maxShownCount)),
                            directoryEntities.Count > maxShownCount ? $"{Environment.NewLine}..." : string.Empty);

                        _ = await DirectoryRepository.AddRangeAsync(directoryEntities);

                        InformationDirectoriesAddingFinished(directoryEntities.Count);

                        // to refresh cached value
                        // cached at first lines of FillParentDirectoriesAsync method
                        _ = await DirectoryRepository.GetAllAsync(useCachedResult: false);
                    }
                    if (fileEntities.Count != 0)
                    {
                        InformationFilesCount(
                            string.Join(Environment.NewLine, fileEntities.Select(static (fe) => fe.FilePhysicalFullPath).Take(maxShownCount)),
                            fileEntities.Count > maxShownCount ? $"{Environment.NewLine}..." : string.Empty);

                        _ = await FileRepository.AddRangeAsync(fileEntities);

                        InformationFilesAddingFinished(fileEntities.Count);

                        // to refresh cached value
                        // cached at first lines of this method
                        var extensions = new HashSet<string>(fileEntities.Select(static (fe) => fe.FileExtension), StringComparer.OrdinalIgnoreCase);
                        foreach (var extension in extensions)
                        {
                            _ = await FileRepository.GetAllFileFullNamesAsync(
                                filterExtension: extension,
                                useCachedResult: false);
                        }
                    }
                }
            }
            finally
            {
                _ = semaphoreRefreshFoundFiles.Release();
            }
        }

        private async Task FillParentDirectoriesAsync(IEnumerable<FileEntity> fileEntities, IEnumerable<DirectoryEntity> directoryEntities)
        {
            var existingDirectoryEntities = (await DirectoryRepository.GetAllAsync(useCachedResult: true)).AsArray();

            foreach (var directoryEntity in directoryEntities)
            {
                if (new DirectoryInfo(directoryEntity.DirectoryFullPath).Parent is DirectoryInfo parentDirectory)
                {
                    directoryEntity.ParentDirectory = directoryEntities.FirstOrDefault(de => de.DirectoryFullPath == parentDirectory.FullName)
                        ?? existingDirectoryEntities.FirstOrDefault(de => de.DirectoryFullPath == parentDirectory.FullName)
                        ?? throw new ApplicationException($"Parent directory not found for directory '{parentDirectory.FullName}'");
                }
                else
                {
                    DebugDirectoryWithoutParent(directoryEntity.DirectoryFullPath);
                }
            }

            foreach (var file in fileEntities)
            {
                file.Directory = directoryEntities.FirstOrDefault(d => d.DirectoryFullPath == file.Folder)
                    ?? existingDirectoryEntities.FirstOrDefault(de => de.DirectoryFullPath == file.Folder)
                    ?? throw new ApplicationException($"Parent directory not found for file '{file.Folder}'");
            }
        }
        public async Task<List<DirectoryEntity>> GetNewDirectoryEntities(IEnumerable<string?> folders)
        {
            var existingDirectories = (await DirectoryRepository
                .GetAllDirectoryFullNamesAsync(useCachedResult: false))
                .AsArray();
            HashSet<string> existingDirectoriesHash = new(existingDirectories, StringComparer.OrdinalIgnoreCase);

            List<DirectoryEntity> newDirectoryEntities = [];
            HashSet<string> alreadyAdded = new(StringComparer.OrdinalIgnoreCase);

            DirectoryInfo? directoryInfo;
            DirectoryEntity directoryEntity;

            foreach (var folder in folders)
            {
                directoryInfo = new(folder!);
                while (directoryInfo?.Exists == true)
                {
                    if (!existingDirectoriesHash.Contains(directoryInfo.FullName)
                        && alreadyAdded.Add(directoryInfo.FullName))
                    {
                        directoryEntity = new()
                        {
                            Directory = directoryInfo.Name,
                            DirectoryFullPath = directoryInfo.FullName,
                            ParentDirectory = null,
                            Depth = DirectoryHelper.GetDirectoryDepth(directoryInfo.FullName),
                        };
                        newDirectoryEntities.Add(directoryEntity);
                    }

                    directoryInfo = directoryInfo.Parent;
                }
            }

            return newDirectoryEntities;
        }
        private async Task<(ReadOnlyMemory<FileEntity> files, ReadOnlyMemory<DirectoryEntity> directories)> GetFilesAndDirectoriesAsync(
            DirectoryEntity? directory,
            bool useCachedResult)
        {
            ReadOnlyMemory<FileEntity> filesItems;
            ReadOnlyMemory<DirectoryEntity> directoryContainers;

            if (directory != null)
            {
                Guid[] parentIds = [directory.Id];

                var filesItemsTask = FileRepository
                    .GetAllByParentDirectoryIdsAsync(parentIds, _serverConfig.ExcludeFolders, useCachedResult);
                var directoryContainersTask = DirectoryRepository
                    .GetAllByParentDirectoryIdsAsync(parentIds, _serverConfig.ExcludeFolders, useCachedResult);

                await Task.WhenAll([filesItemsTask, directoryContainersTask]);

                filesItems = await filesItemsTask;
                directoryContainers = await directoryContainersTask;
            }
            else
            {
                filesItems = ReadOnlyMemory<FileEntity>.Empty;
                directoryContainers = await DirectoryRepository
                    .GetAllByPathFullNamesAsync(_serverConfig.SourceFolders, useCachedResult);
            }

            return (files: filesItems, directories: directoryContainers);
        }
        private Task<(ReadOnlyMemory<FileEntity> files, ReadOnlyMemory<DirectoryEntity> directories)> GetFilesByLastAddedToDbAsync(uint numberOfFiles)
        {
            return FileRepository
                .GetAllByAddedToDbAsync((int)numberOfFiles, _serverConfig.ExcludeFolders, useCachedResult: false)
                .ContinueWith(static (fe) => (fe.Result, ReadOnlyMemory<DirectoryEntity>.Empty));
        }
        public async Task CheckAllFilesExistingAsync(int batchSize = 500)
        {
            FileRepository.DabataseClearChangeTracker();
            int offset = 0;
            ReadOnlyMemory<FileEntity> fileEntities;
            while (true)
            {
                fileEntities = await FileRepository.GetAllAsync(offset, batchSize, withIncludes: false, useCachedResult: false);
                if (fileEntities.IsEmpty)
                {
                    break;
                }

                _ = await CheckFilesExistingAsync(fileEntities);
                offset += fileEntities.Length;
            }
            FileRepository.DabataseClearChangeTracker();
        }
        public async Task<ReadOnlyMemory<FileEntity>> CheckFilesExistingAsync(ReadOnlyMemory<FileEntity> fileEntities)
        {
            if (fileEntities.IsEmpty)
            {
                return fileEntities;
            }

            var fileEntitiesLength = fileEntities.Length;

            using ArrayPoolBufferWriter<FileEntity> existingFilesWriter = new(fileEntitiesLength);
            using ArrayPoolBufferWriter<FileEntity> notExistingFilesWriter = new(fileEntitiesLength);
            //ArrayBufferWriter<FileEntity> existingFilesWriter = new(fileEntitiesLength);
            //ArrayBufferWriter<FileEntity> notExistingFilesWriter = new(fileEntitiesLength);

            try
            {
                int maxDegreeOfParallelism = Math.Max(Math.Min(fileEntitiesLength, (int)_serverConfig.ServerMaxDegreeOfParallelism), 1);

                _ = Parallel.For(
                    0,
                    fileEntitiesLength,
                    parallelOptions: new() { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                    localInit: () =>
                    {
                        // don't forget to ArrayPoolBufferWriter<T>.Dispose();
                        //ArrayPoolBufferWriter<FileEntity> localExisting = new(fileEntitiesLength);
                        //ArrayPoolBufferWriter<FileEntity> localMissing = new(fileEntitiesLength);
                        ArrayBufferWriter<FileEntity> localExisting = new(fileEntitiesLength);
                        ArrayBufferWriter<FileEntity> localMissing = new(fileEntitiesLength);
                        return
                        (
                            existingCount: 0,
                            missingCount: 0,
                            existing: localExisting,
                            missing: localMissing
                        );
                    },
                    body: (index, _, localData) =>
                    {
                        var file = fileEntities.Span[index];
                        if (File.Exists(file.FilePhysicalFullPath))
                        {
                            localData.existing.Write(file);
                        }
                        else
                        {
                            InformationFileMissing(file.FilePhysicalFullPath);
                            localData.missing.Write(file);
                        }
                        return localData;
                    },
                    localFinally: localData =>
                    {
                        existingFilesWriter.Write(localData.existing.WrittenSpan);
                        notExistingFilesWriter.Write(localData.missing.WrittenSpan);
                        localData.existing.Clear();
                        localData.missing.Clear();
                        //localData.existing.Dispose();
                        //localData.missing.Dispose();
                    });

                if (notExistingFilesWriter.WrittenCount == 0)
                {
                    existingFilesWriter.Clear();
                    notExistingFilesWriter.Clear();

                    return fileEntities;
                }
                else
                {
                    var fileEntitiesChecked = await CheckFilesExistingAsyncCore(existingFilesWriter.WrittenMemory, notExistingFilesWriter.WrittenMemory);
                    existingFilesWriter.Clear();
                    notExistingFilesWriter.Clear();

                    return fileEntitiesChecked;
                }
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                return fileEntities;
            }
        }
        private async Task<ReadOnlyMemory<FileEntity>> CheckFilesExistingAsyncCore(
            ReadOnlyMemory<FileEntity> existingFiles,
            ReadOnlyMemory<FileEntity> notExistingFiles)
        {
            await ClearMetadataAsync(notExistingFiles);
            await ClearThumbnailsAsync(notExistingFiles, true);
            _ = await FileRepository.DeleteRangeAsync(notExistingFiles.AsArray());

            return existingFiles;
        }
        public async Task CheckAllDirectoriesExistingAsync(int batchSize = 500)
        {
            DirectoryRepository.DabataseClearChangeTracker();

            int offset = 0;
            ReadOnlyMemory<DirectoryEntity> directoryEntities;
            while (true)
            {
                directoryEntities = await DirectoryRepository.GetAllAsync(offset, batchSize, withIncludes: false, useCachedResult: false);
                if (directoryEntities.IsEmpty)
                {
                    break;
                }

                _ = await CheckDirectoriesExistingAsync(directoryEntities);
                offset += directoryEntities.Length;
            }

            DirectoryRepository.DabataseClearChangeTracker();
        }
        public async Task<ReadOnlyMemory<DirectoryEntity>> CheckDirectoriesExistingAsync(ReadOnlyMemory<DirectoryEntity> directoryEntities)
        {
            if (directoryEntities.IsEmpty)
            {
                return directoryEntities;
            }

            var directoryEntitiesLength = directoryEntities.Length;

            using ArrayPoolBufferWriter<DirectoryEntity> existingDirectoriesWriter = new(directoryEntitiesLength);
            using ArrayPoolBufferWriter<DirectoryEntity> notExistingDirectoriesWriter = new(directoryEntitiesLength);
            //ArrayBufferWriter<DirectoryEntity> existingDirectoriesWriter = new(directoryEntitiesLength);
            //ArrayBufferWriter<DirectoryEntity> notExistingDirectoriesWriter = new(directoryEntitiesLength);

            try
            {
                if (directoryEntities.IsEmpty)
                {
                    return directoryEntities;
                }

                var maxDegreeOfParallelism = Math.Max(Math.Min(directoryEntitiesLength, (int)_serverConfig.ServerMaxDegreeOfParallelism), 1);

                _ = Parallel.For(
                    0,
                    directoryEntitiesLength,
                    parallelOptions: new() { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                    localInit: () =>
                    {
                        // don't forget to ArrayPoolBufferWriter<T>.Dispose();
                        //ArrayPoolBufferWriter<DirectoryEntity> localExisting = new(directoryEntitiesLength);
                        //ArrayPoolBufferWriter<DirectoryEntity> localMissing = new(directoryEntitiesLength);
                        ArrayBufferWriter<DirectoryEntity> localExisting = new(directoryEntitiesLength);
                        ArrayBufferWriter<DirectoryEntity> localMissing = new(directoryEntitiesLength);
                        return (
                            existingCount: 0,
                            missingCount: 0,
                            existing: localExisting,
                            missing: localMissing
                        );
                    },
                    body: (index, _, localData) =>
                    {
                        var directory = directoryEntities.Span[index];
                        if (Directory.Exists(directory.DirectoryFullPath))
                        {
                            localData.existing.Write(directory);
                        }
                        else
                        {
                            InformationDirectoryMissing(directory.DirectoryFullPath);
                            localData.missing.Write(directory);
                        }
                        return localData;
                    },
                    localFinally: localData =>
                    {
                        existingDirectoriesWriter.Write(localData.existing.WrittenSpan);
                        notExistingDirectoriesWriter.Write(localData.missing.WrittenSpan);
                        localData.existing.Clear();
                        localData.missing.Clear();
                        //localData.existing.Dispose();
                        //localData.missing.Dispose();
                    });

                if (notExistingDirectoriesWriter.WrittenCount == 0)
                {
                    existingDirectoriesWriter.Clear();
                    notExistingDirectoriesWriter.Clear();
                    return directoryEntities;
                }
                else
                {
                    var directoryEntitiesChecked = await CheckDirectoriesExistingAsyncCore(existingDirectoriesWriter.WrittenMemory, notExistingDirectoriesWriter.WrittenMemory);
                    existingDirectoriesWriter.Clear();
                    notExistingDirectoriesWriter.Clear();

                    return directoryEntitiesChecked;
                }
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                return directoryEntities;
            }
        }
        private async Task<ReadOnlyMemory<DirectoryEntity>> CheckDirectoriesExistingAsyncCore(
            ReadOnlyMemory<DirectoryEntity> existingDirectories,
            ReadOnlyMemory<DirectoryEntity> notExistingDirectories)
        {
            List<DirectoryEntity> actualNotExisting = [.. notExistingDirectories.AsArray()];

            var notExistingSubdirectories = (await DirectoryRepository
                .GetAllStartingByPathFullNamesAsync(
                    pathFullNames: actualNotExisting.Select(static (ned) => ned.DirectoryFullPath),
                    useCachedResult: false))
                .AsArray();
            if (notExistingSubdirectories.Length != 0)
            {
                actualNotExisting.AddRange(notExistingSubdirectories);
            }

            var removeFiles = (await FileRepository
                .GetAllByParentDirectoryIdsAsync(actualNotExisting.Select(static (ned) => ned.Id), [], useCachedResult: false))
                .AsArray();
            if (removeFiles.Length != 0)
            {
                _ = await CheckFilesExistingAsync(removeFiles);
            }

            _ = await DirectoryRepository.DeleteRangeAsync(actualNotExisting);

            return existingDirectories;
        }
        private async Task CheckParentDirectoriesAsync(string startingPathFullName)
        {
            var filesEntities = (await FileRepository
                .GetAllWithEmptyParentDirectoryIdsAsync(startingPathFullName, _serverConfig.ExcludeFolders, useCachedResult: false))
                .AsArray();
            var directoryEntities = (await DirectoryRepository
                .GetAllWithEmptyParentDirectoryIdsAsync(startingPathFullName, _serverConfig.ExcludeFolders, useCachedResult: false))
                .AsArray();

            if (filesEntities.Length != 0 ||
                directoryEntities.Length != 0)
            {
                HashSet<string> missingDirectoriesFromFiles = new(filesEntities
                    .Where(static (f) => !string.IsNullOrWhiteSpace(f.Folder))
                    .Select(static (f) => f.Folder!),
                    StringComparer.OrdinalIgnoreCase);
                HashSet<string> missingDirectoriesFromDirectories = new(directoryEntities
                    .Where(static (d) => !string.IsNullOrWhiteSpace(d.DirectoryFullPath))
                    .Select(static (d) => d.DirectoryFullPath),
                    StringComparer.OrdinalIgnoreCase);

                var directoryEntitiesMissing = await GetNewDirectoryEntities(missingDirectoriesFromFiles.Union(missingDirectoriesFromDirectories));

                directoryEntities = directoryEntities
                    .Union(directoryEntitiesMissing)
                    .OrderBy(de => de.Depth)
                    .ToArray();

                await FillParentDirectoriesAsync(filesEntities, directoryEntities);

                // adding only new entities, existing only save 
                await DirectoryRepository.AddRangeAsync(directoryEntitiesMissing);

                await DirectoryRepository.SaveChangesAsync();
                await FileRepository.SaveChangesAsync();

                InformationUpdatedParentDirectories(filesEntities.Length, directoryEntities.Length);

                // to refresh cached value
                _ = await DirectoryRepository.GetAllAsync(useCachedResult: false);
            }
        }
        private readonly static Comparison<FileEntity> fileComparison = static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Title, b.Title);
        private readonly static Comparison<DirectoryEntity> directoryComparison = static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Directory, b.Directory);
        public async Task<(ReadOnlyMemory<FileEntity> fileEntities, ReadOnlyMemory<DirectoryEntity> directoryEntities, bool isRootFolder, uint totalMatches)> GetBrowseResultItems(
            string objectID,
            int startingIndex,
            int requestedCount
            )
        {
            ReadOnlyMemory<FileEntity> fileEntities;
            ReadOnlyMemory<DirectoryEntity> directoryEntities;

            var startTime = DateTime.Now;
            var stopwatch = Stopwatch.StartNew();

            var directoryStartObject = await DirectoryRepository.GetByIdAsync(objectID, asNoTracking: true, useCachedResult: true);
            // take a mind, that this time is with included 1st connection to the DB
            var getDirectoryTime = stopwatch.Elapsed.TotalMilliseconds;

            bool isRootFolder = directoryStartObject == null;
            var getAllFilesInDirectoryTime = stopwatch.Elapsed.TotalMilliseconds;
            var refreshFoundFilesTime = stopwatch.Elapsed.TotalMilliseconds;
            var checkParentDirectoriesTime = stopwatch.Elapsed.TotalMilliseconds;
            if (!isRootFolder
                && _serverConfig.SourceFolders.Contains(directoryStartObject!.DirectoryFullPath))
            {
                // refresh directory for added files
                var inputFiles = GetAllFilesInFolders([directoryStartObject!.DirectoryFullPath], true);
                getAllFilesInDirectoryTime = stopwatch.Elapsed.TotalMilliseconds;
                await RefreshFoundFilesAsync(inputFiles, shouldBeAdded: false);
                refreshFoundFilesTime = stopwatch.Elapsed.TotalMilliseconds;
                await CheckParentDirectoriesAsync(directoryStartObject!.DirectoryFullPath);
                checkParentDirectoriesTime = stopwatch.Elapsed.TotalMilliseconds;
            }

            // not possible to pagination for possibility of removed files / directories
            (fileEntities, directoryEntities) = await GetFilesAndDirectoriesAsync(directoryStartObject, useCachedResult: false);
            var getEntitiesTime = stopwatch.Elapsed.TotalMilliseconds;

            // sorting before checking root folder, as for sorting additional files in root folder is by timestamps
            fileEntities.Sort(fileComparison);

            directoryEntities.Sort(directoryComparison);
            var sortEntitiesTime = stopwatch.Elapsed.TotalMilliseconds;

            if (isRootFolder)
            {
                (var fileEntitiesAdded, var directoryEntitiesAdded) = await GetFilesByLastAddedToDbAsync(_serverConfig.CountOfFilesByLastAddedToDb);

                fileEntities = (FileEntity[])[.. fileEntities.Span, .. fileEntitiesAdded.Span];
                directoryEntities = (DirectoryEntity[])[.. directoryEntities.Span, .. directoryEntitiesAdded.Span];
            }
            var addAdditionalEntitiesTime = stopwatch.Elapsed.TotalMilliseconds;

            uint totalMatches = _serverConfig.ServerIgnoreRequestedCountAttributeFromRequest
                ? (uint)(fileEntities.Length + directoryEntities.Length)
                : FilterEntities(startingIndex, requestedCount, ref fileEntities, ref directoryEntities);

            var filterEntitiesTime = stopwatch.Elapsed.TotalMilliseconds;

            // possible to return less objects with this checking, but client will request rest of them in next request
            var countBeforeCheck = fileEntities.Length + directoryEntities.Length;

            fileEntities = await CheckFilesExistingAsync(fileEntities);
            var checkFilesExistingTime = stopwatch.Elapsed.TotalMilliseconds;
            directoryEntities = await CheckDirectoriesExistingAsync(directoryEntities);
            var checkDirectoriesExistingTime = stopwatch.Elapsed.TotalMilliseconds;

            var filledEmptyInfo = await MediaProcessingService.FillEmptyInfoAsync(fileEntities.AsArray(), setCheckedForFailed: false);
            if (filledEmptyInfo
                || countBeforeCheck != (fileEntities.Length + directoryEntities.Length))
            {
                // refresh database data in MemoryCache
                _ = await GetFilesAndDirectoriesAsync(directoryStartObject, useCachedResult: false);
            }

            var fillEmptyData = stopwatch.Elapsed.TotalMilliseconds;
            var endTime = DateTime.Now;

            if (countBeforeCheck != (fileEntities.Length + directoryEntities.Length))
            {
                WarningObjectsRemovedFromDirectory(
                    objectID,
                    directoryStartObject?.DirectoryFullPath,
                    countBeforeCheck,
                    fileEntities.Length + directoryEntities.Length);
            }

            if (_serverConfig.ServerShowDurationDetailsBrowseRequest)
            {
                InformationBrowseDetailInfo(
                    objectID: objectID,
                    startTime: startTime,
                    endTime: endTime,
                    getDirectory: getDirectoryTime,
                    getFilesInDirectory: getAllFilesInDirectoryTime - getDirectoryTime,
                    refreshFoundFilesInDirectory: refreshFoundFilesTime - getAllFilesInDirectoryTime,
                    checkParentDirectories: checkParentDirectoriesTime - refreshFoundFilesTime,
                    getDataFromDatabase: getEntitiesTime - checkParentDirectoriesTime,
                    sortDataFromDatabase: sortEntitiesTime - getEntitiesTime,
                    addAdditionalDataFromDatabase: addAdditionalEntitiesTime - sortEntitiesTime,
                    filterData: filterEntitiesTime - addAdditionalEntitiesTime,
                    checkFiles: checkFilesExistingTime - filterEntitiesTime,
                    checkDirectories: checkDirectoriesExistingTime - checkFilesExistingTime,
                    fillEmptyData: fillEmptyData - checkDirectoriesExistingTime,
                    totalDuration: fillEmptyData,
                    directory: directoryStartObject?.DirectoryFullPath
                    );
            }

            return (fileEntities, directoryEntities, isRootFolder, totalMatches);
        }

        private static uint FilterEntities(int startingIndex, int requestedCount, ref ReadOnlyMemory<FileEntity> fileEntities, ref ReadOnlyMemory<DirectoryEntity> directoryEntities)
        {
            var directoryCount = directoryEntities.Length;
            var fileCount = fileEntities.Length;

            uint totalMatches = (uint)(directoryCount + fileCount);

            int directoryStart = Math.Min(startingIndex, directoryCount);
            int directoryLength = Math.Min(requestedCount, Math.Max(0, directoryCount - directoryStart));
            directoryEntities = directoryEntities.Slice(directoryStart, directoryLength).ToArray();

            int remaining = requestedCount - directoryLength;
            if (remaining > 0)
            {
                int fileStart = Math.Max(startingIndex - directoryCount, 0);
                int fileLength = Math.Min(remaining, Math.Max(0, fileCount - fileStart));
                fileEntities = fileEntities.Slice(fileStart, fileLength).ToArray();
            }
            else
            {
                fileEntities = ReadOnlyMemory<FileEntity>.Empty;
            }

            return totalMatches;
        }
        public async Task ClearAllThumbnailsAsync(bool deleteThumbnailFile = true)
        {
            const int maxChunkSize = 1000;

            int offset = 0;

            FileEntity[] files;
            while (true)
            {
                files = (await FileRepository.GetAllAsync(offset, maxChunkSize, withIncludes: false, useCachedResult: false)).AsArray();
                if (files.Length == 0)
                {
                    break; // Stop if no more files
                }

                await ClearThumbnailsAsync(files, deleteThumbnailFile);
                Array.Clear(files);
                offset += files.Length;
            }

            FileRepository.DabataseClearChangeTracker();
        }
        public async Task ClearThumbnailsAsync(ReadOnlyMemory<FileEntity> files, bool deleteThumbnailFile = true)
        {
            if (deleteThumbnailFile)
            {
                Partitioner.Create(files.AsArray())
                    .AsParallel()
                    .Where(static (f) => f != null
                        && !string.IsNullOrEmpty(f.Thumbnail?.ThumbnailFilePhysicalFullPath))
                    .Select(static (f) => f.Thumbnail!.ThumbnailFilePhysicalFullPath)
                    .Where(File.Exists)
                    .ForAll(File.Delete);
            }

            var filesProperty = files
                .AsArray()
                .Where(static (f) => f != null)
                .Select(static (f) => f.FilePhysicalFullPath)
                .ToHashSet();

            _ = await ThumbnailRepository.ExecuteUpdateAsync(
                predicate: fe => filesProperty.Contains(fe.FilePhysicalFullPath),
                setPropertyCalls: static (fe) => fe
                    .SetProperty(static (fp) => fp.ThumbnailDataId, static (_) => null),
                reloadTrackedEntities: false);
            _ = await FileRepository.ExecuteUpdateAsync(
                predicate: fe => filesProperty.Contains(fe.FilePhysicalFullPath),
                setPropertyCalls: static (fe) => fe
                    .SetProperty(static (fp) => fp.IsThumbnailChecked, static (_) => false)
                    .SetProperty(static (fp) => fp.ThumbnailId, static (_) => null),
                reloadTrackedEntities: false);

            _ = await ThumbnailDataRepository.ExecuteDeleteAsync(
                predicate: md => filesProperty.Contains(md.FilePhysicalFullPath),
                reloadTrackedEntities: false);
            _ = await ThumbnailRepository.ExecuteDeleteAsync(
                predicate: md => filesProperty.Contains(md.FilePhysicalFullPath),
                reloadTrackedEntities: false);

            files
                .AsArray()
                .AsParallel()
                .Where(static (f) => f != null)
                .ForAll(static (fe) =>
                {
                    fe.IsThumbnailChecked = false;
                    fe.ThumbnailId = null;
                    fe.Thumbnail = null;
                });

            _ = await FileRepository.DbContext.Database.ExecuteSqlRawAsync("VACUUM;");
        }
        public async Task ClearAllMetadataAsync()
        {
            const int maxChunkSize = 1000;

            int offset = 0;
            FileEntity[] files;
            while (true)
            {
                files = (await FileRepository.GetAllAsync(offset, maxChunkSize, withIncludes: false, useCachedResult: false)).AsArray();
                if (files.Length == 0)
                {
                    break; // Stop if no more files
                }

                await ClearMetadataAsync(files);
                Array.Clear(files);
                offset += files.Length;
            }

            FileRepository.DabataseClearChangeTracker();
        }
        public async Task ClearMetadataAsync(ReadOnlyMemory<FileEntity> files)
        {
            var filesProperty = files
                .AsArray()
                .Where(static (f) => f != null)
                .Select(static (f) => f.FilePhysicalFullPath)
                .ToHashSet();

            _ = await FileRepository.ExecuteUpdateAsync(
                predicate: fe => filesProperty.Contains(fe.FilePhysicalFullPath),
                setPropertyCalls: static (fe) => fe
                    .SetProperty(static (fp) => fp.IsMetadataChecked, static (_) => false)
                    .SetProperty(static (fp) => fp.AudioMetadataId, static (_) => null)
                    .SetProperty(static (fp) => fp.VideoMetadataId, static (_) => null)
                    .SetProperty(static (fp) => fp.SubtitleMetadataId, static (_) => null),
                reloadTrackedEntities: false);

            _ = await AudioMetadataRepository.ExecuteDeleteAsync(
                predicate: md => filesProperty.Contains(md.FilePhysicalFullPath),
                reloadTrackedEntities: false);
            _ = await SubtitleMetadataRepository.ExecuteDeleteAsync(
                predicate: md => filesProperty.Contains(md.FilePhysicalFullPath),
                reloadTrackedEntities: false);
            _ = await VideoMetadataRepository.ExecuteDeleteAsync(
                predicate: md => filesProperty.Contains(md.FilePhysicalFullPath),
                reloadTrackedEntities: false);

            files
                .AsArray()
                .AsParallel()
                .Where(static (f) => f != null)
                .ForAll(static (fe) =>
                {
                    fe.IsMetadataChecked = false;
                    fe.AudioMetadataId = null;
                    fe.VideoMetadataId = null;
                    fe.SubtitleMetadataId = null;
                    fe.AudioMetadata = null;
                    fe.VideoMetadata = null;
                    fe.SubtitleMetadata = null;
                });

            _ = await FileRepository.DbContext.Database.ExecuteSqlRawAsync("VACUUM;");
        }
    }
}
