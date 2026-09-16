using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NovaWallet.Api.Data;
using NovaWallet.Api.DTOs;
using NovaWallet.Api.Exceptions;
using NovaWallet.Api.Models;
using NovaWallet.Api.Services;
using Testcontainers.PostgreSql;

namespace NovaWallet.Tests;

public class WalletServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("novawallet_service_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    private DbContextOptions<NovaWalletDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _options = new DbContextOptionsBuilder<NovaWalletDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var db = new NovaWalletDbContext(_options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    private WalletService CreateService(NovaWalletDbContext db)
    {
        return new WalletService(
            db,
            NullLogger<WalletService>.Instance);
    }

    [Fact]
    public async Task ReplayingSameIdempotencyKey_DoesNotTransferTwice()
    {
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();

        await using (var setupDb = new NovaWalletDbContext(_options))
        {
            setupDb.Wallets.AddRange(
                new Wallet
                {
                    Id = sourceId,
                    CustomerId = "idempotency-source",
                    BalanceKobo = 1_000_000
                },
                new Wallet
                {
                    Id = destinationId,
                    CustomerId = "idempotency-destination",
                    BalanceKobo = 0
                });

            await setupDb.SaveChangesAsync();
        }

        var request = new TransferRequest
        {
            FromWalletId = sourceId,
            ToWalletId = destinationId,
            AmountKobo = 250_000,
            Description = "Idempotency test"
        };

        TransferResponse first;

        await using (var db = new NovaWalletDbContext(_options))
        {
            first = await CreateService(db).TransferAsync(
                request,
                "same-key-001",
                CancellationToken.None);
        }

        TransferResponse second;

        await using (var db = new NovaWalletDbContext(_options))
        {
            second = await CreateService(db).TransferAsync(
                request,
                "same-key-001",
                CancellationToken.None);
        }

        await using var verificationDb =
            new NovaWalletDbContext(_options);

        var source = await verificationDb.Wallets
            .AsNoTracking()
            .SingleAsync(x => x.Id == sourceId);

        var destination = await verificationDb.Wallets
            .AsNoTracking()
            .SingleAsync(x => x.Id == destinationId);

        var debits = await verificationDb.Transactions
            .CountAsync(x =>
                x.WalletId == sourceId &&
                x.Type == TransactionType.TransferDebit);

        Assert.Equal(first.TransferReference, second.TransferReference);
        Assert.Equal(750_000, source.BalanceKobo);
        Assert.Equal(250_000, destination.BalanceKobo);
        Assert.Equal(1, debits);
    }

    [Fact]
    public async Task ReusingIdempotencyKeyWithDifferentPayload_IsRejected()
    {
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();

        await using (var setupDb = new NovaWalletDbContext(_options))
        {
            setupDb.Wallets.AddRange(
                new Wallet
                {
                    Id = sourceId,
                    CustomerId = "conflict-source",
                    BalanceKobo = 1_000_000
                },
                new Wallet
                {
                    Id = destinationId,
                    CustomerId = "conflict-destination",
                    BalanceKobo = 0
                });

            await setupDb.SaveChangesAsync();
        }

        await using (var firstDb = new NovaWalletDbContext(_options))
        {
            await CreateService(firstDb).TransferAsync(
                new TransferRequest
                {
                    FromWalletId = sourceId,
                    ToWalletId = destinationId,
                    AmountKobo = 100_000
                },
                "conflicting-key-001",
                CancellationToken.None);
        }

        await using var secondDb =
            new NovaWalletDbContext(_options);

        await Assert.ThrowsAsync<IdempotencyConflictException>(
            () => CreateService(secondDb).TransferAsync(
                new TransferRequest
                {
                    FromWalletId = sourceId,
                    ToWalletId = destinationId,
                    AmountKobo = 200_000
                },
                "conflicting-key-001",
                CancellationToken.None));
    }

    [Fact]
    public async Task TransferWithInsufficientFunds_IsRejectedWithoutMutation()
    {
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();

        await using (var setupDb = new NovaWalletDbContext(_options))
        {
            setupDb.Wallets.AddRange(
                new Wallet
                {
                    Id = sourceId,
                    CustomerId = "funds-source",
                    BalanceKobo = 100_000
                },
                new Wallet
                {
                    Id = destinationId,
                    CustomerId = "funds-destination",
                    BalanceKobo = 0
                });

            await setupDb.SaveChangesAsync();
        }

        await using (var transferDb =
            new NovaWalletDbContext(_options))
        {
            await Assert.ThrowsAsync<InsufficientFundsException>(
                () => CreateService(transferDb).TransferAsync(
                    new TransferRequest
                    {
                        FromWalletId = sourceId,
                        ToWalletId = destinationId,
                        AmountKobo = 200_000
                    },
                    "insufficient-key-001",
                    CancellationToken.None));
        }

        await using var verificationDb =
            new NovaWalletDbContext(_options);

        var source = await verificationDb.Wallets
            .AsNoTracking()
            .SingleAsync(x => x.Id == sourceId);

        var destination = await verificationDb.Wallets
            .AsNoTracking()
            .SingleAsync(x => x.Id == destinationId);

        Assert.Equal(100_000, source.BalanceKobo);
        Assert.Equal(0, destination.BalanceKobo);

        Assert.Empty(await verificationDb.Transactions.ToListAsync());
        Assert.Empty(await verificationDb.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task DailyOutboundLimit_IsEnforced()
    {
        var sourceId = Guid.NewGuid();
        var firstDestinationId = Guid.NewGuid();
        var secondDestinationId = Guid.NewGuid();

        await using (var setupDb = new NovaWalletDbContext(_options))
        {
            setupDb.Wallets.AddRange(
                new Wallet
                {
                    Id = sourceId,
                    CustomerId = "limit-source",
                    BalanceKobo = 60_000_000
                },
                new Wallet
                {
                    Id = firstDestinationId,
                    CustomerId = "limit-destination-1"
                },
                new Wallet
                {
                    Id = secondDestinationId,
                    CustomerId = "limit-destination-2"
                });

            await setupDb.SaveChangesAsync();
        }

        await using (var firstDb = new NovaWalletDbContext(_options))
        {
            await CreateService(firstDb).TransferAsync(
                new TransferRequest
                {
                    FromWalletId = sourceId,
                    ToWalletId = firstDestinationId,
                    AmountKobo = 40_000_000
                },
                "daily-limit-key-001",
                CancellationToken.None);
        }

        await using var secondDb =
            new NovaWalletDbContext(_options);

        await Assert.ThrowsAsync<DailyLimitExceededException>(
            () => CreateService(secondDb).TransferAsync(
                new TransferRequest
                {
                    FromWalletId = sourceId,
                    ToWalletId = secondDestinationId,
                    AmountKobo = 11_000_000
                },
                "daily-limit-key-002",
                CancellationToken.None));
    }
    [Fact]
public async Task AuditLog_CannotBeUpdatedOrDeleted()
{
    var walletId = Guid.NewGuid();

    await using (var setupDb = new NovaWalletDbContext(_options))
    {
        setupDb.Wallets.Add(new Wallet
        {
            Id = walletId,
            CustomerId = "immutable-audit-wallet",
            BalanceKobo = 100_000
        });

        setupDb.AuditLogs.Add(new AuditLog
        {
            WalletId = walletId,
            Action = "CREDIT",
            AmountKobo = 100_000,
            BalanceBeforeKobo = 0,
            BalanceAfterKobo = 100_000
        });

        await setupDb.SaveChangesAsync();
    }

    // Attempt to UPDATE an existing audit record.
    await using (var updateDb = new NovaWalletDbContext(_options))
    {
        var audit = await updateDb.AuditLogs.SingleAsync();

        audit.Action = "TAMPERED";

        await Assert.ThrowsAsync<DbUpdateException>(
            () => updateDb.SaveChangesAsync());
    }

    // Verify the failed update did not alter the stored record.
    await using (var verificationDb = new NovaWalletDbContext(_options))
    {
        var audit = await verificationDb.AuditLogs
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal("CREDIT", audit.Action);
        Assert.Equal(100_000, audit.AmountKobo);
        Assert.Equal(0, audit.BalanceBeforeKobo);
        Assert.Equal(100_000, audit.BalanceAfterKobo);
    }

    // Attempt to DELETE the audit record.
    await using (var deleteDb = new NovaWalletDbContext(_options))
    {
        var audit = await deleteDb.AuditLogs.SingleAsync();

        deleteDb.AuditLogs.Remove(audit);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => deleteDb.SaveChangesAsync());
    }

    // It must still exist.
    await using var finalDb = new NovaWalletDbContext(_options);

    Assert.Equal(1, await finalDb.AuditLogs.CountAsync());
}
}
