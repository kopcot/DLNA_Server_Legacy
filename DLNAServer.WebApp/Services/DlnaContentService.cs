using DLNAServer.WebApp.Services.Interfaces;
using OpenTelemetry.Trace;

namespace DLNAServer.WebApp.Services
{
    public class DlnaContentService : BaseHttpClientService<DlnaContentService>, IDlnaContentService
    {
        public DlnaContentService(
            string? addressIP,
            string? routeAPI,
            HttpClient httpClient,
            ILogger<DlnaContentService> logger,
            IHttpContextAccessor httpContextAccessor,
            Tracer tracer)
            : base(addressIP, routeAPI, httpClient, logger, httpContextAccessor, tracer)
        {
        }
    }
}
