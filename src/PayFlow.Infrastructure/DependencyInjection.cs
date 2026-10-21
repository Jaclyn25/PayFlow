using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.Common.Options;
using PayFlow.Infrastructure.BackgroundJobs;
using PayFlow.Infrastructure.Caching;
using PayFlow.Infrastructure.Messaging;
using PayFlow.Infrastructure.Persistence;
using PayFlow.Infrastructure.Reporting;
using PayFlow.Infrastructure.Security;
using PayFlow.Infrastructure.Services;

namespace PayFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // DB Context
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured. Set it in appsettings.json or environment variables.");

        services.AddDbContext<PayFlowDbContext>(options =>
            options.UseNpgsql(connectionString, b => b.MigrationsAssembly(typeof(PayFlowDbContext).Assembly.FullName)));

        services.AddScoped<IPayFlowDbContext>(provider => provider.GetRequiredService<PayFlowDbContext>());

        // Security & Auth Options
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<FeeOptions>(configuration.GetSection(FeeOptions.SectionName));
        services.Configure<KycOptions>(configuration.GetSection(KycOptions.SectionName));
        services.Configure<TwoFactorOptions>(configuration.GetSection(TwoFactorOptions.SectionName));
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddSingleton<IExchangeRateService, ExchangeRateService>();
        services.AddSingleton<IStatementReportGenerator, StatementReportGenerator>();
        services.AddScoped<ITwoFactorAuthService, TwoFactorAuthService>();

        // Webhook Signing & Dispatching
        services.AddHttpClient("Webhooks", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddSingleton<IWebhookSigner, WebhookSigner>();
        services.AddScoped<IWebhookDispatcherService, WebhookDispatcherService>();

        // Redis Caching
        var redisConnectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = "PayFlow_";
        });
        services.AddSingleton<ICacheService, RedisCacheService>();

        // RabbitMQ & Messaging
        services.Configure<RabbitMQOptions>(configuration.GetSection(RabbitMQOptions.SectionName));
        services.AddSingleton<IRabbitMQPublisher, RabbitMQPublisher>();
        services.AddScoped<INotificationMessageProcessor, NotificationMessageProcessor>();
        services.AddScoped<IDeadLetterQueueService, DeadLetterQueueService>();

        // Background Workers
        services.AddHostedService<OutboxBackgroundWorker>();
        services.AddHostedService<NotificationConsumer>();
        services.AddHostedService<WebhookDispatcherWorker>();

        return services;
    }
}
