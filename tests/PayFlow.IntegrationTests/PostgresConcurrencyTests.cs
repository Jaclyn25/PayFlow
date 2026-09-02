using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Application.Services;
using PayFlow.Domain.Constants;
using PayFlow.Domain.Entities;
using PayFlow.Domain.Exceptions;
using PayFlow.Infrastructure.Persistence;
using PayFlow.IntegrationTests.Fixtures;
using Xunit;

namespace PayFlow.IntegrationTests;

public class PostgresConcurrencyTests : IClassFixture<PostgresTestContainerFixture>
{
    private readonly PostgresTestContainerFixture _fixture;

    public PostgresConcurrencyTests(PostgresTestContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RealPostgres_ScenarioA_ConcurrentTransfers_DoubleSpendingProtection()
    {
        // Arrange
        var options = _fixture.CreateDbContextOptions();

        Guid aliceUserId;
        Guid bobUserId;
        string bobEmail;

        using (var setupContext = new PayFlowDbContext(options))
        {
            var alice = new User { FirstName = "Alice", LastName = "Postgres", Email = $"alice_{Guid.NewGuid():N}@payflow.local", PasswordHash = "HASH" };
            var aliceWallet = new Wallet { UserId = alice.Id, Balance = 1000.00m, Currency = "EGP" };
            alice.Wallet = aliceWallet;

            var bob = new User { FirstName = "Bob", LastName = "Postgres", Email = $"bob_{Guid.NewGuid():N}@payflow.local", PasswordHash = "HASH" };
            var bobWallet = new Wallet { UserId = bob.Id, Balance = 500.00m, Currency = "EGP" };
            bob.Wallet = bobWallet;

            setupContext.Users.AddRange(alice, bob);
            setupContext.Wallets.AddRange(aliceWallet, bobWallet);
            await setupContext.SaveChangesAsync();

            aliceUserId = alice.Id;
            bobUserId = bob.Id;
            bobEmail = bob.Email;
        }

        var request1 = new TransferRequest(bobEmail, 800.00m, "Concurrent Transfer A");
        var request2 = new TransferRequest(bobEmail, 800.00m, "Concurrent Transfer B");

        var key1 = Guid.NewGuid().ToString();
        var key2 = Guid.NewGuid().ToString();

        // Act: Fire 2 concurrent HTTP transfer requests against REAL PostgreSQL container
        var task1 = Task.Run(async () =>
        {
            using var context1 = new PayFlowDbContext(options);
            var service1 = new TransferService(context1, new Mock<ICacheService>().Object);
            return await service1.TransferAsync(aliceUserId, key1, request1);
        });

        var task2 = Task.Run(async () =>
        {
            using var context2 = new PayFlowDbContext(options);
            var service2 = new TransferService(context2, new Mock<ICacheService>().Object);
            return await service2.TransferAsync(aliceUserId, key2, request2);
        });

        int successCount = 0;
        int failureCount = 0;

        try
        {
            await Task.WhenAll(task1, task2);
        }
        catch
        {
            // Expected that 1 task fails with InsufficientBalance Exception
        }

        foreach (var task in new[] { task1, task2 })
        {
            if (task.IsCompletedSuccessfully)
            {
                successCount++;
            }
            else if (task.IsFaulted)
            {
                var baseEx = task.Exception?.GetBaseException();
                if (baseEx is InsufficientBalanceException || baseEx is DomainException)
                {
                    failureCount++;
                }
            }
        }

        // Assert: Real PostgreSQL row-level locking guarantees 1 succeeds, 1 fails
        successCount.Should().Be(1, "Only one transfer of 800 EGP can succeed from a 1000 EGP balance.");
        failureCount.Should().Be(1, "The second concurrent transfer must fail due to insufficient funds.");

        using var verifyContext = new PayFlowDbContext(options);
        var aliceWalletFinal = await verifyContext.Wallets.FirstAsync(w => w.UserId == aliceUserId);
        var bobWalletFinal = await verifyContext.Wallets.FirstAsync(w => w.UserId == bobUserId);

        aliceWalletFinal.Balance.Should().Be(200.00m, "1000 - 800 = 200 EGP remaining balance on Real PostgreSQL.");
        bobWalletFinal.Balance.Should().Be(1300.00m, "500 + 800 = 1300 EGP final balance for Bob.");

        var transactions = await verifyContext.Transactions.Where(t => t.SenderWalletId == aliceWalletFinal.Id).ToListAsync();
        transactions.Should().HaveCount(1, "Exactly one completed transfer record exists.");

        var outboxMessages = await verifyContext.OutboxMessages.ToListAsync();
        outboxMessages.Should().HaveCount(1, "Exactly one OutboxMessage was created.");
    }

    [Fact]
    public async Task RealPostgres_ScenarioB_ConcurrentTransfers_IdempotencyRaceCondition()
    {
        // Arrange
        var options = _fixture.CreateDbContextOptions();

        Guid aliceUserId;
        string bobEmail;

        using (var setupContext = new PayFlowDbContext(options))
        {
            var alice = new User { FirstName = "AliceIdem", LastName = "Test", Email = $"alice_idem_{Guid.NewGuid():N}@payflow.local", PasswordHash = "HASH" };
            var aliceWallet = new Wallet { UserId = alice.Id, Balance = 1000.00m, Currency = "EGP" };
            alice.Wallet = aliceWallet;

            var bob = new User { FirstName = "BobIdem", LastName = "Test", Email = $"bob_idem_{Guid.NewGuid():N}@payflow.local", PasswordHash = "HASH" };
            var bobWallet = new Wallet { UserId = bob.Id, Balance = 500.00m, Currency = "EGP" };
            bob.Wallet = bobWallet;

            setupContext.Users.AddRange(alice, bob);
            setupContext.Wallets.AddRange(aliceWallet, bobWallet);
            await setupContext.SaveChangesAsync();

            aliceUserId = alice.Id;
            bobEmail = bob.Email;
        }

        var request = new TransferRequest(bobEmail, 400.00m, "Idempotent Transfer");
        var sharedIdempotencyKey = $"CONCURRENT_KEY_{Guid.NewGuid():N}";

        // Act: Fire 2 concurrent requests with the SAME Idempotency-Key against REAL PostgreSQL
        var task1 = Task.Run(async () =>
        {
            using var context1 = new PayFlowDbContext(options);
            var service1 = new TransferService(context1, new Mock<ICacheService>().Object);
            return await service1.TransferAsync(aliceUserId, sharedIdempotencyKey, request);
        });

        var task2 = Task.Run(async () =>
        {
            using var context2 = new PayFlowDbContext(options);
            var service2 = new TransferService(context2, new Mock<ICacheService>().Object);
            return await service2.TransferAsync(aliceUserId, sharedIdempotencyKey, request);
        });

        var results = await Task.WhenAll(task1, task2);

        // Assert: Both tasks receive the exact same Transaction response ID
        results[0].Should().NotBeNull();
        results[1].Should().NotBeNull();
        results[0].Id.Should().Be(results[1].Id, "Both concurrent requests with same key must return identical transaction.");

        using var verifyContext = new PayFlowDbContext(options);
        var finalAliceWallet = await verifyContext.Wallets.FirstAsync(w => w.UserId == aliceUserId);
        
        // Money deducted EXACTLY ONCE (1000 - 400 = 600 EGP), NOT twice!
        finalAliceWallet.Balance.Should().Be(600.00m, "Database unique constraint on (UserId, Key) prevents double-deduction under concurrent requests.");

        var txns = await verifyContext.Transactions.Where(t => t.IdempotencyKey == sharedIdempotencyKey).ToListAsync();
        txns.Should().HaveCount(1, "Exactly one Transaction record created.");

        var ledgers = await verifyContext.LedgerEntries.Where(l => l.TransactionId == txns[0].Id).ToListAsync();
        ledgers.Should().HaveCount(2, "Exactly one double-entry ledger pair created.");

        var outbox = await verifyContext.OutboxMessages.ToListAsync();
        outbox.Should().HaveCount(1, "Exactly one OutboxMessage created.");
    }

    [Fact]
    public async Task RealPostgres_ScenarioC_ConcurrentCrossTransfers_DeterministicLocking()
    {
        // Arrange: Alice and Bob cross-transfer concurrently
        var options = _fixture.CreateDbContextOptions();

        Guid aliceUserId;
        Guid bobUserId;
        string aliceEmail;
        string bobEmail;

        using (var setupContext = new PayFlowDbContext(options))
        {
            var alice = new User { FirstName = "AliceDL", LastName = "Test", Email = $"alice_dl_{Guid.NewGuid():N}@payflow.local", PasswordHash = "HASH" };
            var aliceWallet = new Wallet { UserId = alice.Id, Balance = 1000.00m, Currency = "EGP" };
            alice.Wallet = aliceWallet;

            var bob = new User { FirstName = "BobDL", LastName = "Test", Email = $"bob_dl_{Guid.NewGuid():N}@payflow.local", PasswordHash = "HASH" };
            var bobWallet = new Wallet { UserId = bob.Id, Balance = 1000.00m, Currency = "EGP" };
            bob.Wallet = bobWallet;

            setupContext.Users.AddRange(alice, bob);
            setupContext.Wallets.AddRange(aliceWallet, bobWallet);
            await setupContext.SaveChangesAsync();

            aliceUserId = alice.Id;
            bobUserId = bob.Id;
            aliceEmail = alice.Email;
            bobEmail = bob.Email;
        }

        var requestAliceToBob = new TransferRequest(bobEmail, 200.00m, "Alice -> Bob");
        var requestBobToAlice = new TransferRequest(aliceEmail, 300.00m, "Bob -> Alice");

        var keyA = Guid.NewGuid().ToString();
        var keyB = Guid.NewGuid().ToString();

        // Act: Run both cross-transfers concurrently
        var taskAlice = Task.Run(async () =>
        {
            using var context1 = new PayFlowDbContext(options);
            var service1 = new TransferService(context1, new Mock<ICacheService>().Object);
            return await service1.TransferAsync(aliceUserId, keyA, requestAliceToBob);
        });

        var taskBob = Task.Run(async () =>
        {
            using var context2 = new PayFlowDbContext(options);
            var service2 = new TransferService(context2, new Mock<ICacheService>().Object);
            return await service2.TransferAsync(bobUserId, keyB, requestBobToAlice);
        });

        await Task.WhenAll(taskAlice, taskBob);

        // Assert: Deterministic lock ordering prevents deadlocks, both succeed!
        taskAlice.IsCompletedSuccessfully.Should().BeTrue();
        taskBob.IsCompletedSuccessfully.Should().BeTrue();

        using var verifyContext = new PayFlowDbContext(options);
        var finalAliceWallet = await verifyContext.Wallets.FirstAsync(w => w.UserId == aliceUserId);
        var finalBobWallet = await verifyContext.Wallets.FirstAsync(w => w.UserId == bobUserId);

        // Alice: 1000 - 200 + 300 = 1100 EGP
        // Bob:   1000 + 200 - 300 = 900 EGP
        finalAliceWallet.Balance.Should().Be(1100.00m);
        finalBobWallet.Balance.Should().Be(900.00m);
    }
}
