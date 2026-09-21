using DLNAServer.Common;
using DLNAServer.Database.Entities;
using DLNAServer.Database.Repositories.Interfaces;
using DLNAServer.Helpers.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DLNAServer.Database.Repositories
{
    public sealed class DirectoryRepository : BaseRepository<DirectoryEntity>, IDirectoryRepository
    {
        public DirectoryRepository(DlnaDbContext dbContext, IMemoryCache memoryCache, ILogger<DirectoryRepository> logger)
            : base(dbContext, memoryCache, logger, nameof(DirectoryRepository))
        {
            DefaultOrderBy = static (entities) => entities
                .OrderBy(static (d) => d.LC_DirectoryFullPath)
                .ThenByDescending(static (d) => d.CreatedInDB);
            DefaultInclude = static (entities) => entities
                .Include(static (d) => d.ParentDirectory);
        }
        public Task<ReadOnlyMemory<DirectoryEntity>> GetAllByParentDirectoryIdsAsync(IEnumerable<Guid> expectedDirectories, IEnumerable<string> excludeFolders, bool useCachedResult = true)
        {
            var queryAction = DbSet
                //.AsNoTracking()
                .Where(d => d.ParentDirectoryId != null);
            foreach (var expectedDirectory in expectedDirectories)
            {
                queryAction = queryAction.Where(d => d.ParentDirectoryId.Equals(expectedDirectory));
            }
            foreach (var excludeFolder in excludeFolders)
            {
                var exclude = excludeFolder.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture);
                queryAction = queryAction.Where(d => !EF.Functions.Collate(d.LC_DirectoryFullPath, "NOCASE").Contains(exclude));
            }
            queryAction = queryAction
                .IncludeChildEntities(DefaultInclude)
                .OrderEntitiesByDefault(DefaultOrderBy);

            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: queryAction,
                cacheKey: GetCacheKey<DirectoryEntity[]>(expectedDirectories.Select(static (e) => e.ToString())),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<DirectoryEntity>> GetAllByParentDirectoryIdsAsync(IEnumerable<string> expectedDirectories, IEnumerable<string> excludeFolders, bool useCachedResult = true)
        {
            return GetAllByParentDirectoryIdsAsync(expectedDirectories.Select(static (ed) => Guid.TryParse(ed, out var dbGuid) ? dbGuid : Guid.Empty), excludeFolders, useCachedResult);
        }
        public Task<ReadOnlyMemory<string>> GetAllDirectoryFullNamesAsync(bool useCachedResult = true)
        {
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .AsNoTracking()
                    .OrderEntitiesByDefault(DefaultOrderBy)
                    .Select(static (d) => d.DirectoryFullPath),
                cacheKey: GetCacheKey<string[]>(),
                cacheDuration: TimeSpanValues.TimeMin5,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<DirectoryEntity>> GetAllWithEmptyParentDirectoryIdsAsync(string pathFullName, IEnumerable<string> excludeFolders, bool useCachedResult = true)
        {
            pathFullName = pathFullName.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture);
            int pathFullNameLength = pathFullName.Length;

            var exclude = excludeFolders.Select(static (ef) => ef.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .Where(de => de.ParentDirectoryId == null
                        && de.LC_DirectoryFullPath.Length >= pathFullNameLength
                        && EF.Functions.Collate(de.LC_DirectoryFullPath, "NOCASE").StartsWith(pathFullName)
                        && !exclude.Any(ef => EF.Functions.Collate(de.LC_DirectoryFullPath, "NOCASE").Contains(ef)))
                    .IncludeChildEntities(DefaultInclude)
                    .OrderEntitiesByDefault(DefaultOrderBy),
                cacheKey: GetCacheKey<DirectoryEntity[]>(excludeFolders.Select(static (ed) => ed.ToString()).Union([pathFullName])),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<DirectoryEntity>> GetAllByDirectoryDepthAsync(int depth, bool useCachedResult = true)
        {
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .Where(d => d.Depth == depth)
                    .IncludeChildEntities(DefaultInclude)
                    .OrderEntitiesByDefault(DefaultOrderBy),
                cacheKey: GetCacheKey<DirectoryEntity[]>([depth.ToString()]),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<DirectoryEntity>> GetAllByDirectoryDepthAsync(int depth, int skip, int take, bool useCachedResult = true)
        {
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .Where(d => d.Depth == depth)
                    .IncludeChildEntities(DefaultInclude)
                    .OrderEntitiesByDefault(DefaultOrderBy),
                cacheKey: GetCacheKey<DirectoryEntity[]>([depth.ToString(), skip.ToString(), take.ToString()]),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<DirectoryEntity>> GetAllStartingByPathFullNameAsync(string pathFullName, bool useCachedResult = true)
        {
            pathFullName = pathFullName.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture);
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .Where(d => EF.Functions.Collate(d.LC_DirectoryFullPath, "NOCASE").Equals(pathFullName)
                        || EF.Functions.Collate(d.LC_DirectoryFullPath, "NOCASE").StartsWith(pathFullName + Path.DirectorySeparatorChar))
                    .IncludeChildEntities(DefaultInclude)
                    .OrderEntitiesByDefault(DefaultOrderBy),
                cacheKey: GetCacheKey<DirectoryEntity[]>([pathFullName]),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<DirectoryEntity>> GetAllStartingByPathFullNamesAsync(IEnumerable<string> pathFullNames, bool useCachedResult = true)
        {
            pathFullNames = pathFullNames.Select(static (p) => p.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .Where(d => pathFullNames.Any(p => EF.Functions.Collate(d.LC_DirectoryFullPath, "NOCASE").Equals(p))
                        || pathFullNames.Any(p => EF.Functions.Collate(d.LC_DirectoryFullPath, "NOCASE").StartsWith(p + Path.DirectorySeparatorChar)))
                    .IncludeChildEntities(DefaultInclude)
                    .OrderEntitiesByDefault(DefaultOrderBy),
                cacheKey: GetCacheKey<DirectoryEntity[]>(pathFullNames),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<DirectoryEntity>> GetAllByPathFullNamesAsync(IEnumerable<string> pathFullNames, bool asNoTracking = false, bool useCachedResult = true)
        {
            pathFullNames = pathFullNames.Select(static (p) => p.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: asNoTracking
                    ? DbSet
                        .AsNoTracking()
                        .Where(d => pathFullNames.Any(p => EF.Functions.Collate(d.LC_DirectoryFullPath, "NOCASE").Equals(p)))
                        .IncludeChildEntities(DefaultInclude)
                        .OrderEntitiesByDefault(DefaultOrderBy)
                    : DbSet
                        .Where(d => pathFullNames.Any(p => EF.Functions.Collate(d.LC_DirectoryFullPath, "NOCASE").Equals(p)))
                        .IncludeChildEntities(DefaultInclude)
                        .OrderEntitiesByDefault(DefaultOrderBy),
                cacheKey: GetCacheKey<DirectoryEntity[]>(pathFullNames),
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
                    .AsNoTracking()
                    .Where(d => pathFullNames.Any(p => EF.Functions.Collate(d.LC_DirectoryFullPath, "NOCASE").Equals(p)))
                    .Select(static (d) => d.DirectoryFullPath),
                cacheKey: GetCacheKey<DirectoryEntity[]>(pathFullNames),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
    }
}
