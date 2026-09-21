using DLNAServer.Common;
using DLNAServer.Database.Entities;
using DLNAServer.Database.Repositories.Interfaces;
using DLNAServer.Helpers.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DLNAServer.Database.Repositories
{
    public sealed class FileRepository : BaseRepository<FileEntity>, IFileRepository
    {
        public FileRepository(DlnaDbContext dbContext, IMemoryCache memoryCache, ILogger<FileRepository> logger)
            : base(dbContext, memoryCache, logger, nameof(FileRepository))
        {
            DefaultOrderBy = static (entities) => entities
                .OrderBy(static (f) => f.LC_FilePhysicalFullPath)
                .ThenByDescending(static (f) => f.CreatedInDB);
            DefaultInclude = static (entities) => entities
                        .Include(static (f) => f.Directory)
                        .Include(static (f) => f.AudioMetadata)
                        .Include(static (f) => f.VideoMetadata)
                        .Include(static (f) => f.SubtitleMetadata)
                        .Include(static (f) => f.Thumbnail);
        }
        public new Task<bool> AddAsync(FileEntity entity)
        {
            return AddRangeAsync([entity]);
        }
        public new async Task<bool> AddRangeAsync(IEnumerable<FileEntity> entities)
        {
            var autoDetectChangesEnabled = DbContext.ChangeTracker.AutoDetectChangesEnabled;
            DbContext.ChangeTracker.AutoDetectChangesEnabled = false;

            try
            {
                await using (var transaction = await DbContext.Database.BeginTransactionAsync())
                {
                    DbContext.AttachRange(
                        entities
                            .Where(e => e.Directory != null
                                && !IsEntityTracked(e.Directory))
                            .Select(static (e) => e.Directory!)
                            .ToArray());
                    DbContext.AttachRange(
                        entities
                            .Where(e => e.AudioMetadata != null
                                && !IsEntityTracked(e.AudioMetadata))
                            .Select(static (e) => e.AudioMetadata!)
                            .ToArray());
                    DbContext.AttachRange(
                        entities
                            .Where(e => e.VideoMetadata != null
                                && !IsEntityTracked(e.VideoMetadata))
                            .Select(static (e) => e.VideoMetadata!)
                            .ToArray());
                    DbContext.AttachRange(
                        entities
                            .Where(e => e.SubtitleMetadata != null
                                && !IsEntityTracked(e.SubtitleMetadata))
                            .Select(static (e) => e.SubtitleMetadata!)
                            .ToArray());
                    DbContext.AttachRange(
                        entities
                            .Where(e => e.Thumbnail != null
                                && !IsEntityTracked(e.Thumbnail))
                            .Select(static (e) => e.Thumbnail!)
                            .ToArray());
                    DbContext.AttachRange(
                        entities
                            .Where(e => e.Thumbnail?.ThumbnailData != null
                                && !IsEntityTracked(e.Thumbnail?.ThumbnailData))
                            .Select(static (e) => e.Thumbnail!.ThumbnailData!)
                            .ToArray());

                    DbSet.AddRange(entities);

                    _ = await DbContext.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
            }
            finally
            {
                DbContext.ChangeTracker.AutoDetectChangesEnabled = autoDetectChangesEnabled;
            }
            return true;
        }
        public Task<ReadOnlyMemory<FileEntity>> GetAllByAddedToDbAsync(int takeNumber, IEnumerable<string> excludeFolders, bool useCachedResult = true)
        {
            var exclude = excludeFolders.Select(static (ef) => ef.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .Where(fe => !exclude.Any(ef => EF.Functions.Collate(fe.LC_FilePhysicalFullPath, "NOCASE").Contains(ef)))
                    .IncludeChildEntities(DefaultInclude)
                    .OrderByDescending(static (f) => f.CreatedInDB)
                    .Take(takeNumber),
                cacheKey: GetCacheKey<FileEntity[]>([takeNumber.ToString()]),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<FileEntity>> GetAllByParentDirectoryIdsAsync(IEnumerable<Guid> expectedDirectories, IEnumerable<string> excludeFolders, bool useCachedResult = true)
        {
            var queryAction = DbSet
                //.AsNoTracking()
                .Where(fe => fe.DirectoryId != null);
            foreach (var expectedDirectory in expectedDirectories)
            {
                queryAction = queryAction.Where(fe => fe.DirectoryId.Equals(expectedDirectory));
            }
            foreach (var excludeFolder in excludeFolders)
            {
                var exclude = excludeFolder.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture);
                queryAction = queryAction.Where(fe => !EF.Functions.Collate(fe.LC_FilePhysicalFullPath, "NOCASE").Contains(excludeFolder));
            }
            queryAction = queryAction
                .IncludeChildEntities(DefaultInclude)
                .OrderEntitiesByDefault(DefaultOrderBy);

            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: queryAction,
                cacheKey: GetCacheKey<FileEntity[]>(expectedDirectories.Select(static (ed) => ed.ToString())),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }

        public Task<ReadOnlyMemory<FileEntity>> GetAllByParentDirectoryIdsAsync(IEnumerable<string> expectedDirectories, IEnumerable<string> excludeFolders, bool useCachedResult = true)
        {
            return GetAllByParentDirectoryIdsAsync(expectedDirectories.Select(static (ed) => Guid.TryParse(ed, out var dbGuid) ? dbGuid : Guid.Empty), excludeFolders, useCachedResult);
        }
        public Task<ReadOnlyMemory<FileEntity>> GetAllWithEmptyParentDirectoryIdsAsync(string pathFullName, IEnumerable<string> excludeFolders, bool useCachedResult = true)
        {
            pathFullName = pathFullName.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture);
            int pathFullNameLength = pathFullName.Length;

            var exclude = excludeFolders.Select(static (ef) => ef.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .Where(fe => fe.DirectoryId == null
                        && fe.LC_FilePhysicalFullPath.Length >= pathFullNameLength
                        && EF.Functions.Collate(fe.LC_FilePhysicalFullPath, "NOCASE").StartsWith(pathFullName)
                        && !exclude.Any(ef => EF.Functions.Collate(fe.LC_FilePhysicalFullPath, "NOCASE").Contains(ef)))
                    .IncludeChildEntities(DefaultInclude)
                    .OrderEntitiesByDefault(DefaultOrderBy),
                cacheKey: GetCacheKey<FileEntity[]>(excludeFolders.Union([pathFullName])),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<string>> GetAllFileFullNamesAsync(string? filterExtension = null, bool useCachedResult = true)
        {
            var query = DbSet
                    .AsNoTracking();
            if (!string.IsNullOrWhiteSpace(filterExtension))
            {
                filterExtension = filterExtension.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture);
                query = query
                    .Where(f => EF.Functions.Collate(f.LC_FileExtension, "NOCASE").Equals(filterExtension));
            }
            query = query
                    .OrderEntitiesByDefault(DefaultOrderBy);
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: query.Select(static (f) => f.FilePhysicalFullPath),
                cacheKey: GetCacheKey<string[]>(
                    additionalArgs: !string.IsNullOrWhiteSpace(filterExtension) ? [filterExtension] : null,
                    methodName: nameof(GetAllFileFullNamesAsync)),
                cacheDuration: TimeSpanValues.TimeMin30,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public async Task<(bool ok, int? minDepth)> GetMinimalDepthAsync(bool useCachedResult = true)
        {
            var minDepth = await GetSingleWithCacheAsync(
                queryAction: DbSet
                    .AsNoTracking()
                    .Include(static (f) => f.Directory)
                    .MinAsync(static (f) => f.Directory != null ? f.Directory.Depth : short.MaxValue),
                cacheKey: GetCacheKey<int>(methodName: nameof(GetMinimalDepthAsync)),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return minDepth == short.MaxValue ? (false, null) : (true, minDepth);
        }
        public Task<ReadOnlyMemory<FileEntity>> GetAllByDirectoryDepthAsync(int depth, bool useCachedResult = true)
        {
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .AsNoTracking()
                    .Include(static (f) => f.Directory)
                    .Where(f => f.Directory != null
                        && f.Directory.Depth == depth)
                    .OrderEntitiesByDefault(DefaultOrderBy),
                cacheKey: GetCacheKey<FileEntity[]>([depth.ToString()]),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<FileEntity>> GetAllByDirectoryDepthAsync(int depth, int skip, int take, bool useCachedResult = true)
        {
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .AsNoTracking()
                    .Include(static (f) => f.Directory)
                    .Where(f => f.Directory != null
                        && f.Directory.Depth == depth)
                    .OrderEntitiesByDefault(DefaultOrderBy)
                    .Skip(skip)
                    .Take(take),
                cacheKey: GetCacheKey<FileEntity[]>([depth.ToString(), skip.ToString(), take.ToString()]),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<FileEntity>> GetAllByPathFullNameAsync(string pathFullName, bool useCachedResult = true)
        {
            pathFullName = pathFullName.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture);
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .Where(f => EF.Functions.Collate(f.LC_FilePhysicalFullPath, "NOCASE").Equals(pathFullName))
                    .IncludeChildEntities(DefaultInclude)
                    .OrderEntitiesByDefault(DefaultOrderBy),
                cacheKey: GetCacheKey<FileEntity[]>([pathFullName]),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<string>> GetAllExistingByPathFullNamesAsync(IEnumerable<string> pathFullNames, bool useCachedResult = true)
        {
            pathFullNames = pathFullNames.Select(static (p) => p.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .Where(d => pathFullNames.Any(p => EF.Functions.Collate(d.LC_FilePhysicalFullPath, "NOCASE").Equals(p)))
                    .Select(d => d.FilePhysicalFullPath),
                cacheKey: GetCacheKey<DirectoryEntity[]>(pathFullNames),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
    }
}
