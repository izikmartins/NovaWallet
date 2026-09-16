namespace NovaWallet.Api.DTOs;

public record WalletResponse(
    Guid Id,
    string CustomerId,
    long BalanceKobo,
    string Currency,
    DateTimeOffset CreatedAt);

public record BalanceResponse(
    Guid WalletId,
    long BalanceKobo,
    string Currency);

public record CreditResponse(
    Guid TransactionId,
    Guid WalletId,
    long AmountKobo,
    long BalanceKobo,
    string Currency);

public record TransferResponse(
    Guid TransferReference,
    Guid FromWalletId,
    Guid ToWalletId,
    long AmountKobo,
    string Currency,
    string Status);

public record TransactionResponse(
    Guid Id,
    string Type,
    long AmountKobo,
    long BalanceAfterKobo,
    Guid? TransferReference,
    Guid? CounterpartyWalletId,
    string? Description,
    DateTimeOffset CreatedAt);

public record StatementResponse(
    Guid WalletId,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<TransactionResponse> Transactions);
