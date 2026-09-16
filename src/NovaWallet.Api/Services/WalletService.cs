using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovaWallet.Api.Data;
using NovaWallet.Api.DTOs;
using NovaWallet.Api.Exceptions;
using NovaWallet.Api.Models;

namespace NovaWallet.Api.Services;

public class WalletService : IWalletService
{
    private const long DailyTransferLimitKobo = 50_000_000;

    private readonly NovaWalletDbContext _db;
    private readonly ILogger<WalletService> _logger;

    public WalletService(
        NovaWalletDbContext db,
        ILogger<WalletService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<WalletResponse> CreateWalletAsync(
        CreateWalletRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = request.CustomerId.Trim();

        if (string.IsNullOrWhiteSpace(customerId))
            throw new InvalidTransferException("CustomerId is required.");

        var exists = await _db.Wallets
            .AnyAsync(x => x.CustomerId == customerId, cancellationToken);

        if (exists)
            throw new InvalidTransferException(
                "A wallet already exists for this customer.");

        var wallet = new Wallet
        {
            CustomerId = customerId
        };

        _db.Wallets.Add(wallet);
        await _db.SaveChangesAsync(cancellationToken);

        return new WalletResponse(
            wallet.Id,
            wallet.CustomerId,
            wallet.BalanceKobo,
            wallet.Currency,
            wallet.CreatedAt);
    }

    public async Task<BalanceResponse> GetBalanceAsync(
        Guid walletId,
        CancellationToken cancellationToken)
    {
        var wallet = await _db.Wallets
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == walletId, cancellationToken)
            ?? throw new WalletNotFoundException(walletId);

        return new BalanceResponse(
            wallet.Id,
            wallet.BalanceKobo,
            wallet.Currency);
    }

    public async Task<CreditResponse> CreditAsync(
        Guid walletId,
        CreditWalletRequest request,
        CancellationToken cancellationToken)
    {
        if (request.AmountKobo <= 0)
            throw new InvalidTransferException(
                "Credit amount must be greater than zero.");

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

        var wallet = await _db.Wallets
            .FromSqlInterpolated(
                $@"SELECT * FROM ""Wallets""
                   WHERE ""Id"" = {walletId}
                   FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new WalletNotFoundException(walletId);

        var balanceBefore = wallet.BalanceKobo;

        try
        {
            checked
            {
                wallet.BalanceKobo += request.AmountKobo;
            }
        }
        catch (OverflowException)
        {
            throw new InvalidTransferException("Amount is too large.");
        }

        wallet.UpdatedAt = DateTimeOffset.UtcNow;

        var ledgerTransaction = new LedgerTransaction
        {
            WalletId = wallet.Id,
            Type = TransactionType.Credit,
            AmountKobo = request.AmountKobo,
            BalanceAfterKobo = wallet.BalanceKobo,
            Description = request.Description
        };

        _db.Transactions.Add(ledgerTransaction);

        _db.AuditLogs.Add(new AuditLog
        {
            WalletId = wallet.Id,
            Action = "CREDIT",
            AmountKobo = request.AmountKobo,
            BalanceBeforeKobo = balanceBefore,
            BalanceAfterKobo = wallet.BalanceKobo
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CreditResponse(
            ledgerTransaction.Id,
            wallet.Id,
            request.AmountKobo,
            wallet.BalanceKobo,
            wallet.Currency);
    }

    public async Task<TransferResponse> TransferAsync(
        TransferRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new InvalidTransferException(
                "Idempotency-Key header is required.");

        if (idempotencyKey.Length > 200)
            throw new InvalidTransferException(
                "Idempotency-Key cannot exceed 200 characters.");

        if (request.AmountKobo <= 0)
            throw new InvalidTransferException(
                "Transfer amount must be greater than zero.");

        if (request.FromWalletId == request.ToWalletId)
            throw new InvalidTransferException(
                "Source and destination wallets must be different.");

        var requestHash = ComputeRequestHash(request);

        var existing = await _db.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Key == idempotencyKey,
                cancellationToken);

        if (existing is not null)
        {
            if (existing.RequestHash != requestHash)
                throw new IdempotencyConflictException();

            return new TransferResponse(
                existing.TransferReference,
                request.FromWalletId,
                request.ToWalletId,
                request.AmountKobo,
                "NGN",
                "Completed");
        }

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

        // Lock wallets in deterministic ID order to reduce deadlock risk.
        var firstId = request.FromWalletId.CompareTo(request.ToWalletId) < 0
            ? request.FromWalletId
            : request.ToWalletId;

        var secondId = firstId == request.FromWalletId
            ? request.ToWalletId
            : request.FromWalletId;

        var firstWallet = await LockWalletAsync(firstId, cancellationToken);
        var secondWallet = await LockWalletAsync(secondId, cancellationToken);

        var fromWallet = firstWallet.Id == request.FromWalletId
            ? firstWallet
            : secondWallet;

        var toWallet = firstWallet.Id == request.ToWalletId
            ? firstWallet
            : secondWallet;

        if (fromWallet.BalanceKobo < request.AmountKobo)
            throw new InsufficientFundsException();

        var (watStartUtc, watEndUtc) = GetCurrentWatDayUtcRange();

        var outboundToday = await _db.Transactions
            .Where(x =>
                x.WalletId == fromWallet.Id &&
                x.Type == TransactionType.TransferDebit &&
                x.CreatedAt >= watStartUtc &&
                x.CreatedAt < watEndUtc)
            .SumAsync(x => (long?)x.AmountKobo, cancellationToken) ?? 0;

        long projectedOutbound;

        try
        {
            projectedOutbound = checked(outboundToday + request.AmountKobo);
        }
        catch (OverflowException)
        {
            throw new DailyLimitExceededException();
        }

        if (projectedOutbound > DailyTransferLimitKobo)
            throw new DailyLimitExceededException();

        var fromBefore = fromWallet.BalanceKobo;
        var toBefore = toWallet.BalanceKobo;

        fromWallet.BalanceKobo -= request.AmountKobo;

        try
        {
            checked
            {
                toWallet.BalanceKobo += request.AmountKobo;
            }
        }
        catch (OverflowException)
        {
            throw new InvalidTransferException(
                "Destination wallet balance would exceed the supported range.");
        }

        var now = DateTimeOffset.UtcNow;
        fromWallet.UpdatedAt = now;
        toWallet.UpdatedAt = now;

        var transferReference = Guid.NewGuid();

        _db.Transactions.AddRange(
            new LedgerTransaction
            {
                WalletId = fromWallet.Id,
                Type = TransactionType.TransferDebit,
                AmountKobo = request.AmountKobo,
                BalanceAfterKobo = fromWallet.BalanceKobo,
                TransferReference = transferReference,
                CounterpartyWalletId = toWallet.Id,
                Description = request.Description,
                CreatedAt = now
            },
            new LedgerTransaction
            {
                WalletId = toWallet.Id,
                Type = TransactionType.TransferCredit,
                AmountKobo = request.AmountKobo,
                BalanceAfterKobo = toWallet.BalanceKobo,
                TransferReference = transferReference,
                CounterpartyWalletId = fromWallet.Id,
                Description = request.Description,
                CreatedAt = now
            });

        _db.AuditLogs.AddRange(
            new AuditLog
            {
                WalletId = fromWallet.Id,
                Action = "TRANSFER_DEBIT",
                AmountKobo = request.AmountKobo,
                BalanceBeforeKobo = fromBefore,
                BalanceAfterKobo = fromWallet.BalanceKobo,
                TransferReference = transferReference,
                CreatedAt = now
            },
            new AuditLog
            {
                WalletId = toWallet.Id,
                Action = "TRANSFER_CREDIT",
                AmountKobo = request.AmountKobo,
                BalanceBeforeKobo = toBefore,
                BalanceAfterKobo = toWallet.BalanceKobo,
                TransferReference = transferReference,
                CreatedAt = now
            });

        _db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Key = idempotencyKey,
            RequestHash = requestHash,
            TransferReference = transferReference,
            CreatedAt = now
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);

            // A concurrent request may have inserted the same key.
            _db.ChangeTracker.Clear();

            var concurrentRecord = await _db.IdempotencyRecords
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Key == idempotencyKey,
                    cancellationToken);

            if (concurrentRecord is not null)
            {
                if (concurrentRecord.RequestHash != requestHash)
                    throw new IdempotencyConflictException();

                return new TransferResponse(
                    concurrentRecord.TransferReference,
                    request.FromWalletId,
                    request.ToWalletId,
                    request.AmountKobo,
                    "NGN",
                    "Completed");
            }

            throw;
        }

        _logger.LogInformation(
            "Transfer {TransferReference} completed from {FromWalletId} to {ToWalletId}",
            transferReference,
            fromWallet.Id,
            toWallet.Id);

        return new TransferResponse(
            transferReference,
            fromWallet.Id,
            toWallet.Id,
            request.AmountKobo,
            "NGN",
            "Completed");
    }

    public async Task<StatementResponse> GetStatementAsync(
        Guid walletId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1)
            page = 1;

        pageSize = Math.Clamp(pageSize, 1, 100);

        var walletExists = await _db.Wallets
            .AnyAsync(x => x.Id == walletId, cancellationToken);

        if (!walletExists)
            throw new WalletNotFoundException(walletId);

        var query = _db.Transactions
            .AsNoTracking()
            .Where(x => x.WalletId == walletId);

        var totalCount = await query.CountAsync(cancellationToken);

        var transactions = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new TransactionResponse(
                x.Id,
                x.Type.ToString(),
                x.AmountKobo,
                x.BalanceAfterKobo,
                x.TransferReference,
                x.CounterpartyWalletId,
                x.Description,
                x.CreatedAt))
            .ToListAsync(cancellationToken);

        return new StatementResponse(
            walletId,
            page,
            pageSize,
            totalCount,
            transactions);
    }

    private async Task<Wallet> LockWalletAsync(
        Guid walletId,
        CancellationToken cancellationToken)
    {
        return await _db.Wallets
            .FromSqlInterpolated(
                $@"SELECT * FROM ""Wallets""
                   WHERE ""Id"" = {walletId}
                   FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new WalletNotFoundException(walletId);
    }

    private static string ComputeRequestHash(TransferRequest request)
    {
        var normalized = new
        {
            request.FromWalletId,
            request.ToWalletId,
            request.AmountKobo,
            Description = request.Description ?? string.Empty
        };

        var json = JsonSerializer.Serialize(normalized);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));

        return Convert.ToHexString(bytes);
    }

    private static (DateTimeOffset StartUtc, DateTimeOffset EndUtc)
        GetCurrentWatDayUtcRange()
    {
        // West Africa Time is UTC+1 year-round.
        var watOffset = TimeSpan.FromHours(1);
        var nowWat = DateTimeOffset.UtcNow.ToOffset(watOffset);

        var startWat = new DateTimeOffset(
            nowWat.Year,
            nowWat.Month,
            nowWat.Day,
            0,
            0,
            0,
            watOffset);

        return (
            startWat.ToUniversalTime(),
            startWat.AddDays(1).ToUniversalTime());
    }
}
