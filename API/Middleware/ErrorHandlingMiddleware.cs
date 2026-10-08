using TranslationBackend.Application.Interface.IService;

namespace TranslationBackend.API.Middleware
{
    /// <summary>
    /// One error shape for the translation endpoints: <c>{ "code": "...", "message": "..." }</c>.
    /// A <see cref="JobException"/> carries its own status and code; anything unexpected is logged and answered with 500
    /// without leaking details. (The sign-in and employee endpoints keep their SupplyChain-compatible shapes.)
    /// </summary>
    public class ErrorHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ErrorHandlingMiddleware> _logger;

        public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (JobException ex)
            {
                await Write(context, ex.StatusCode, ex.Code, ex.Message);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // the client went away; nothing to answer
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in {Method} {Path}", context.Request.Method, context.Request.Path);
                await Write(context, StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "An unexpected error occurred.");
            }
        }

        private static Task Write(HttpContext context, int status, string code, string message)
        {
            if (context.Response.HasStarted) return Task.CompletedTask;
            context.Response.Clear();
            context.Response.StatusCode = status;
            return context.Response.WriteAsJsonAsync(new { code, message });
        }
    }
}
