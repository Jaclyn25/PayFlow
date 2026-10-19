using System.Net;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PayFlow.Domain.Exceptions;

namespace PayFlow.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred while processing request '{Path}'.", context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";

        var (statusCode, title, detail, errors) = exception switch
        {
            ValidationException valEx => (
                HttpStatusCode.BadRequest,
                "Validation Error",
                "One or more validation failures occurred.",
                valEx.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray()
                    )
            ),

            InsufficientBalanceException insEx => (
                HttpStatusCode.BadRequest,
                "Insufficient Balance",
                insEx.Message,
                null
            ),

            KycLimitExceededException kycEx => (
                HttpStatusCode.UnprocessableEntity,
                "KYC Limit Exceeded",
                kycEx.Message,
                new Dictionary<string, string[]>
                {
                    ["kyc"] = new[] { kycEx.RequiredAction },
                    ["limitType"] = new[] { kycEx.LimitType },
                    ["limitAmount"] = new[] { kycEx.LimitAmount.ToString("F2") },
                    ["currentUsage"] = new[] { kycEx.CurrentUsage.ToString("F2") }
                }
            ),

            InvalidTransactionException invEx => (
                HttpStatusCode.BadRequest,
                "Invalid Transaction Request",
                invEx.Message,
                null
            ),

            DuplicateIdempotencyException dupEx => (
                HttpStatusCode.Conflict,
                "Duplicate Request",
                dupEx.Message,
                null
            ),

            UnauthorizedDomainException unAuthEx => (
                HttpStatusCode.Unauthorized,
                "Unauthorized",
                unAuthEx.Message,
                null
            ),

            UserNotFoundException unfEx => (
                HttpStatusCode.NotFound,
                "User Not Found",
                unfEx.Message,
                null
            ),

            WalletNotFoundException wnfEx => (
                HttpStatusCode.NotFound,
                "Wallet Not Found",
                wnfEx.Message,
                null
            ),

            DbUpdateConcurrencyException => (
                HttpStatusCode.Conflict,
                "Concurrency Conflict",
                "The resource was modified concurrently by another transaction. Please retry your request.",
                null
            ),

            _ => (
                HttpStatusCode.InternalServerError,
                "Internal Server Error",
                "An unexpected internal error occurred. Please contact support.",
                null
            )
        };

        context.Response.StatusCode = (int)statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = (int)statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };

        if (errors != null)
        {
            problemDetails.Extensions["errors"] = errors;
        }

        var json = JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
