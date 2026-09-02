using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Domain.Constants;
using PayFlow.Domain.Entities;

namespace PayFlow.Infrastructure.Persistence;

public static class DbInitializer
{
    public static async Task SeedDevelopmentDataAsync(IServiceProvider serviceProvider, IHostEnvironment environment)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PayFlowDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<PayFlowDbContext>>();

        try
        {
            await context.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not run database migration automatically on startup. Ensure database server is reachable.");
            return;
        }

        // Ensure System Treasury Wallet exists in all environments
        await EnsureSystemWalletExistsAsync(context);

        if (!environment.IsDevelopment())
        {
            return;
        }

        if (await context.Users.AnyAsync(u => u.Email == "alice@payflow.local"))
        {
            return; // Already seeded
        }

        logger.LogInformation("Seeding Development demo data for Alice and Bob...");

        var defaultPasswordHash = passwordHasher.HashPassword("Password123!");

        var alice = new User
        {
            FirstName = "Alice",
            LastName = "Smith",
            Email = "alice@payflow.local",
            PasswordHash = defaultPasswordHash,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var aliceWallet = new Wallet
        {
            UserId = alice.Id,
            Balance = 1000.00m,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        alice.Wallet = aliceWallet;

        var bob = new User
        {
            FirstName = "Bob",
            LastName = "Jones",
            Email = "bob@payflow.local",
            PasswordHash = defaultPasswordHash,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var bobWallet = new Wallet
        {
            UserId = bob.Id,
            Balance = 500.00m,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        bob.Wallet = bobWallet;

        context.Users.AddRange(alice, bob);
        context.Wallets.AddRange(aliceWallet, bobWallet);

        await context.SaveChangesAsync();
        logger.LogInformation("Development demo users seeded successfully (alice@payflow.local / bob@payflow.local).");
    }

    private static async Task EnsureSystemWalletExistsAsync(PayFlowDbContext context)
    {
        var exists = await context.Wallets.AnyAsync(w => w.Id == SystemConstants.SystemWalletId);
        if (!exists)
        {
            var systemUser = new User
            {
                Id = SystemConstants.SystemUserId,
                FirstName = "System",
                LastName = "Treasury",
                Email = "system.treasury@payflow.local",
                PasswordHash = "LOCKED_SYSTEM_ACCOUNT",
                IsActive = false,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            var systemWallet = new Wallet
            {
                Id = SystemConstants.SystemWalletId,
                UserId = systemUser.Id,
                Balance = 0.00m,
                Currency = "EGP",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            context.Users.Add(systemUser);
            context.Wallets.Add(systemWallet);
            await context.SaveChangesAsync();
        }
    }
}
