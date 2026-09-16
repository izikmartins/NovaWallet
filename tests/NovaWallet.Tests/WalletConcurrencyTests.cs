using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NovaWallet.Api.Data;
using NovaWallet.Api.DTOs;
using NovaWallet.Api.Exceptions;
using NovaWallet.Api.Models;
using NovaWallet.Api.Services;
using Testcontainers.PostgreSql;

namespace NovaWallet.Tests;

public class WalletConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("novawallet_test")
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

    [Fact]
    public async Task ConcurrentTransfers_MustNeverOverdrawSourceWallet()
    {
        // Arrange: source contains exactly 1,000,000 kobo.
        // Twenty transfers simultaneously attempt to spend
        // 100,000 kobo each. Only ten may succeed.
        var sourceId = Guid.NewGuid();

        var destinationIds = Enumerable
            .Range(0, 20)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        await using (var setupDb = new NovaWalletDbContext(_options))
        {
            setupDb.Wallets.Add(new Wallet
            {
                Id = sourceId,
                CustomerId = "concurrency-source",
                BalanceKobo = 1_000_000
            });

            for (var i = 0; i < destinationIds.Length; i++)
            {
                setupDb.Wallets.Add(new Wallet
                {
                    Id = destinationIds[i],
                    CustomerId = $"concurrency-destination-{i}",
                    BalanceKobo = 0
                });
            }

            await setupDb.SaveChangesAsync();
        }

        var startGate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = destinationIds
            .Select((destinationId, index) =>
                Task.Run(async () =>
                {
                    await startGate.Task;

                    await using var db =
                        new NovaWalletDbContext(_options);

                    var service = new WalletService(
                        db,
                        NullLogger<WalletService>.Instance);

                    try
                    {
                        await service.TransferAsync(
                            new TransferRequest
                            {
                                FromWalletId = sourceId,
                                ToWalletId = destinationId,
                                AmountKobo = 100_000,
                                Description = "Concurrency test"
                            },
                            $"concurrency-key-{index}",
                            CancellationToken.None);

                        return true;
                    }
                    catch (InsufficientFundsException)
                    {
                        return false;
                    }
                }))
            .ToArray();

        // Release all twenty requests together.
        startGate.SetResult(true);

        var results = await Task.WhenAll(tasks);

        // Assert.
        await using var verificationDb =
            new NovaWalletDbContext(_options);

        var source = await verificationDb.Wallets
            .AsNoTracking()
            .SingleAsync(x => x.Id == sourceId);

        var destinationsTotal = await verificationDb.Wallets
            .Where(x => destinationIds.Contains(x.Id))
            .SumAsync(x => x.BalanceKobo);

        var debitCount = await verificationDb.Transactions
            .CountAsync(x =>
                x.WalletId == sourceId &&
                x.Type == TransactionType.TransferDebit);

        var auditDebitCount = await verificationDb.AuditLogs
            .CountAsync(x =>
                x.WalletId == sourceId &&
                x.Action == "TRANSFER_DEBIT");

        Assert.Equal(10, results.Count(x => x));
        Assert.Equal(10, results.Count(x => !x));

        Assert.Equal(0, source.BalanceKobo);
        Assert.True(source.BalanceKobo >= 0);

        Assert.Equal(1_000_000, destinationsTotal);

        Assert.Equal(10, debitCount);
        Assert.Equal(10, auditDebitCount);
    }
}
