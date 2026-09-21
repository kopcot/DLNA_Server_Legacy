using E = DLNAServer.Database.Entities;
using System.Net;

namespace DLNAServer.WebApp.Services.Interfaces
{
    public interface IDlnaContentService
    {
        Task<(bool, HttpStatusCode?, Exception?, (E.FileEntity[]?, E.DirectoryEntity[]?, uint)?)> GetDlnaContentAsync<T>(CancellationToken? cancellationToken = null);
    }
}
