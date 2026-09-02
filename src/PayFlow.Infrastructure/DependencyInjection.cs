using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Infrastructure.BackgroundJobs;
using PayFlow.Infrastructure.Caching;
using PayFlow.Infrastructure.Messaging;
using PayFlow.Infrastructure.Persistence;
using PayFlow.Infrastructure.Security;

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
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

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

        // Background Workers
        services.AddHostedService<OutboxBackgroundWorker>();
        services.AddHostedService<NotificationConsumer>();

        return services;
    }
}
