namespace NovaWallet.Api.Models;

public class AuditLog
{
    public long Id { get; set; }

    public Guid WalletId { get; set; }

    public string Action { get; set; } = string.Empty;

    public long AmountKobo { get; set; }

    public long BalanceBeforeKobo { get; set; }

    public long BalanceAfterKobo { get; set; }

    public Guid? TransferReference { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
