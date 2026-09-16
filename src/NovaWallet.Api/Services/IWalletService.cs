using NovaWallet.Api.DTOs;

namespace NovaWallet.Api.Services;

public interface IWalletService
{
    Task<WalletResponse> CreateWalletAsync(
        CreateWalletRequest request,
        CancellationToken cancellationToken);

    Task<BalanceResponse> GetBalanceAsync(
        Guid walletId,
        CancellationToken cancellationToken);

    Task<CreditResponse> CreditAsync(
        Guid walletId,
        CreditWalletRequest request,
        CancellationToken cancellationToken);

    Task<TransferResponse> TransferAsync(
        TransferRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<StatementResponse> GetStatementAsync(
        Guid walletId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
