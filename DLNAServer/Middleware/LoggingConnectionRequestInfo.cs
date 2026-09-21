using DLNAServer.Helpers.Logger;
using System.Diagnostics;

namespace DLNAServer.Middleware
{
    public partial class LoggingConnectionRequestInfo
    {
        private readonly RequestDelegate _next;
        private readonly ILogger _logger;

        public LoggingConnectionRequestInfo(RequestDelegate next, ILogger<LoggingConnectionRequestInfo> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            
            var connection = context.Connection;
            LogConnectionInfo(
                connection.Id,
                connection.RemoteIpAddress,
                connection.RemotePort,
                connection.LocalIpAddress,
                connection.LocalPort,
                context.Request.Scheme,
                context.Request.Method,
                context.Request.Path.Value,
                context.Request.QueryString.Value);

            await _next(context);

            LogConnectionFinishedInfo(
                connection.Id,
                stopwatch.Elapsed);
        }
    }
}
