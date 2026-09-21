using DLNAServer.Configuration;
using DLNAServer.Features.FileWatcher.Interfaces;
using DLNAServer.Features.MediaContent.Interfaces;
using DLNAServer.SOAP.Endpoints;
using DLNAServer.SOAP.Endpoints.Responses.ContentDirectory;
using Microsoft.AspNetCore.Mvc;

namespace DLNAServer.Controllers.Media
{
    [Route("[controller]")]
    [ApiController]
    public class ContentController : ControllerBase
    {
        private readonly Lazy<IContentExplorerManager> _contentExplorerLazy;
        private readonly ILogger<ContentController> _logger;
        private IContentExplorerManager ContentExplorer => _contentExplorerLazy.Value;
        public ContentController(
            Lazy<IContentExplorerManager> contentExplorerLazy,
            ILogger<ContentController> logger)
        {
            _contentExplorerLazy = contentExplorerLazy;
            _logger = logger;
        }

        [HttpGet("browse/{objectID}")]
        public async Task<IActionResult> Browse([FromRoute] string objectID, [FromQuery] int? startingIndex, [FromQuery] int? requestedCount)
        {
            requestedCount ??= int.MaxValue;

            requestedCount = Math.Max(requestedCount.Value, 1);

            (var fileEntities, var directoryEntities, _, uint totalMatches) = await ContentExplorer.GetBrowseResultItems(objectID, startingIndex ?? 0, requestedCount.Value);

            return Ok(new { directoryEntities, fileEntities, totalMatches});
        }
    }
}
