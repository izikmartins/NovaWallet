using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NovaWallet.Api.Exceptions;

namespace NovaWallet.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
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
        catch (Exception exception)
        {
            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (status, title) = exception switch
        {
            WalletNotFoundException => (StatusCodes.Status404NotFound, "Wallet not found"),
            InsufficientFundsException => (StatusCodes.Status409Conflict, "Insufficient funds"),
            DailyLimitExceededException => (StatusCodes.Status409Conflict, "Daily limit exceeded"),
            IdempotencyConflictException => (StatusCodes.Status409Conflict, "Idempotency conflict"),
            InvalidTransferException => (StatusCodes.Status400BadRequest, "Invalid transfer"),
            DbUpdateException => (StatusCodes.Status409Conflict, "Database conflict"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        if (status >= 500)
            _logger.LogError(exception, "Unhandled exception");
        else
            _logger.LogWarning(exception, "Request failed with status {StatusCode}", status);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status == 500
                ? "An unexpected error occurred while processing the request."
                : exception.Message,
            Instance = context.Request.Path
        };

        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(problem);
    }
}
