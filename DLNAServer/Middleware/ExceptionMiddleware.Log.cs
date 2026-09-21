namespace DLNAServer.Middleware
{
    public partial class ExceptionMiddleware
    {
        [LoggerMessage(1, LogLevel.Critical, "Unhandled exeption: {exceptionMessage}\n{exceptionStack}")]
        partial void ErrorUnhandledException(string exceptionMessage, string? exceptionStack, Exception ex);
    }
}
