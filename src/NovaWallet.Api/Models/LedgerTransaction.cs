namespace NovaWallet.Api.Models;

public enum TransactionType
{
    Credit,
    TransferDebit,
    TransferCredit
}

public class LedgerTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WalletId { get; set; }

    public TransactionType Type { get; set; }

    public long AmountKobo { get; set; }

    public long BalanceAfterKobo { get; set; }

    public Guid? TransferReference { get; set; }

    public Guid? CounterpartyWalletId { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Wallet Wallet { get; set; } = null!;
}
