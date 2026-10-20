using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PayFlow.Api.Hubs;
using PayFlow.Api.Middleware;
using PayFlow.Api.Services;
using PayFlow.Api.Swagger;
using PayFlow.Application;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Infrastructure;
using PayFlow.Infrastructure.Persistence;
using PayFlow.Infrastructure.Security;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 1. Serilog Setup
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("ApplicationName", "PayFlow.Api")
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// 2. Register Application & Infrastructure Services
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// 3. JWT Authentication Configuration
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var key = Encoding.UTF8.GetBytes(jwtOptions.SecretKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = jwtOptions.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtOptions.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/wallet"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PayFlow.Domain.Constants.RoleConstants.RequireAdminRole, policy =>
        policy.RequireRole(
            PayFlow.Domain.Constants.RoleConstants.SystemAdmin,
            PayFlow.Domain.Constants.RoleConstants.Admin));

    options.AddPolicy(PayFlow.Domain.Constants.RoleConstants.RequireAuditorRole, policy =>
        policy.RequireRole(
            PayFlow.Domain.Constants.RoleConstants.ComplianceOfficer,
            PayFlow.Domain.Constants.RoleConstants.Auditor,
            PayFlow.Domain.Constants.RoleConstants.SystemAdmin,
            PayFlow.Domain.Constants.RoleConstants.Admin));

    options.AddPolicy(PayFlow.Domain.Constants.RoleConstants.RequireElevatedRole, policy =>
        policy.RequireRole(
            PayFlow.Domain.Constants.RoleConstants.SystemAdmin,
            PayFlow.Domain.Constants.RoleConstants.ComplianceOfficer,
            PayFlow.Domain.Constants.RoleConstants.Admin,
            PayFlow.Domain.Constants.RoleConstants.Auditor));
});

// 4. Rate Limiting Configuration
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("AuthPolicy", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });
});

// 5. OpenTelemetry Setup
builder.Services.AddOpenTelemetry()
    .WithTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("PayFlow.Api"))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation();
    })
    .WithMetrics(metricsProviderBuilder =>
    {
        metricsProviderBuilder
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("PayFlow.Api"))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
    });

// 6. Health Checks Configuration
var dbConnection = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured. Set it in appsettings.json or environment variables.");
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";

builder.Services.AddHealthChecks()
    .AddNpgSql(dbConnection, name: "postgresql", tags: new[] { "db", "ready" })
    .AddRedis(redisConnection, name: "redis", tags: new[] { "cache", "ready" });

// 7. SignalR & Real-Time Notifications Configuration
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.HandshakeTimeout = TimeSpan.FromSeconds(15);
    options.MaximumParallelInvocationsPerClient = 5;
    options.StatefulReconnectBufferSize = 100_000;
});
builder.Services.AddSingleton<IUserIdProvider, CustomUserIdProvider>();
builder.Services.AddScoped<IRealTimeNotificationService, SignalRNotificationService>();

// 8. Controllers & Swagger Configuration
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "PayFlow API — Digital Wallet & Payment Platform",
        Version = "v1",
        Description = "Production-style digital wallet backend with safe money transfers, idempotency, double-entry ledger, and outbox messaging."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
        }
    });

    options.OperationFilter<IdempotencyHeaderOperationFilter>();
});

// 9. CORS Configuration
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173", "http://localhost:3000" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("PayFlowCorsPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// 10. Development Database Seeding
await DbInitializer.SeedDevelopmentDataAsync(app.Services, app.Environment);

// 11. Middleware Pipeline
app.UseSerilogRequestLogging();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "PayFlow API v1"));
}

app.UseRouting();
app.UseCors("PayFlowCorsPolicy");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<WalletHub>("/hubs/wallet");

// Health Check Endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/readiness", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapHealthChecks("/liveness", new HealthCheckOptions
{
    Predicate = _ => false
});

try
{
    Log.Information("Starting PayFlow API host...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
