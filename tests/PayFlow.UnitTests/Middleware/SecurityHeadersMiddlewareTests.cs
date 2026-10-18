using FluentAssertions;
using Microsoft.AspNetCore.Http;
using PayFlow.Api.Middleware;
using Xunit;

namespace PayFlow.UnitTests.Middleware;

public class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AddsOwaspSecurityHeaders()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Headers["Server"] = "Kestrel";
        context.Response.Headers["X-Powered-By"] = "ASP.NET";

        var middleware = new SecurityHeadersMiddleware(next: (ctx) =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        headers["X-Frame-Options"].ToString().Should().Be("DENY");
        headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");
        headers["X-XSS-Protection"].ToString().Should().Be("0");
        headers["Content-Security-Policy"].ToString().Should().Be("default-src 'self'; frame-ancestors 'none';");
        headers["Permissions-Policy"].ToString().Should().Be("geolocation=(), camera=(), microphone=()");
        headers.ContainsKey("Server").Should().BeFalse();
        headers.ContainsKey("X-Powered-By").Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_HttpsRequest_AddsStrictTransportSecurity()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";

        var middleware = new SecurityHeadersMiddleware(next: (ctx) =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Headers["Strict-Transport-Security"].ToString().Should().Be("max-age=31536000; includeSubDomains");
    }
}
