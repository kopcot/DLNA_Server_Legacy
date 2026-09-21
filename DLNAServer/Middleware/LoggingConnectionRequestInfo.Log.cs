using System.Net;

namespace DLNAServer.Middleware
{
    public partial class LoggingConnectionRequestInfo
    {
        [LoggerMessage(1, LogLevel.Information, "{connectionId}, remote address {remoteIpAddress}:{remotePort}, local address {localIpAddress}:{localPort}, scheme: '{scheme}', method: '{method}', path: '{path}', query: '{query}'")]
        partial void LogConnectionInfo(
            string connectionId,
            IPAddress? remoteIpAddress,
            int? remotePort,
            IPAddress? localIpAddress,
            int? localPort,
            string? scheme,
            string? method,
            string? path,
            string? query);


        [LoggerMessage(2, LogLevel.Information, "{connectionId}, request finished in {time}")]
        partial void LogConnectionFinishedInfo(
            string connectionId,
            TimeSpan time);
    }
}
