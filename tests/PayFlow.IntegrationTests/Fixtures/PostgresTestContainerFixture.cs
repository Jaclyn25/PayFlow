using Microsoft.EntityFrameworkCore;
using PayFlow.Domain.Constants;
using PayFlow.Domain.Entities;
using PayFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace PayFlow.IntegrationTests.Fixtures;

public class PostgresTestContainerFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString => _container?.GetConnectionString() 
        ?? throw new InvalidOperationException("PostgreSQL Testcontainer is not initialized.");

    public DbContextOptions<PayFlowDbContext> CreateDbContextOptions()
    {
        if (_container == null)
        {
            throw new InvalidOperationException("PostgreSQL Testcontainers require a running Docker daemon (npipe://./pipe/docker_engine). Docker daemon was unavailable.");
        }

        return new DbContextOptionsBuilder<PayFlowDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("payflow_test_db")
                .WithUsername("postgres")
                .WithPassword("postgrespassword")
                .Build();

            await _container.StartAsync();

            using var context = new PayFlowDbContext(CreateDbContextOptions());
            await context.Database.MigrateAsync();
            SeedSystemWallet(context);
        }
        catch (Exception ex)
        {
            _container = null;
            throw new InvalidOperationException($"[SKIP/FAIL] Real PostgreSQL Testcontainers require a running Docker daemon. Docker API connection failed: {ex.Message}", ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    private static void SeedSystemWallet(PayFlowDbContext context)
    {
        if (!context.Users.Any(u => u.Id == SystemConstants.SystemUserId))
        {
            var sysUser = new User
            {
                Id = SystemConstants.SystemUserId,
                FirstName = "System",
                LastName = "Treasury",
                Email = "system.treasury@payflow.local",
                PasswordHash = "LOCKED_SYSTEM_ACCOUNT",
                IsActive = false
            };

            var sysWallet = new Wallet
            {
                Id = SystemConstants.SystemWalletId,
                UserId = sysUser.Id,
                Balance = 0m,
                Currency = "EGP"
            };

            sysUser.Wallet = sysWallet;

            context.Users.Add(sysUser);
            context.Wallets.Add(sysWallet);
            context.SaveChanges();
        }
    }
}
