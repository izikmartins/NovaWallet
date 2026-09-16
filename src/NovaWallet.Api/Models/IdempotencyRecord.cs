namespace NovaWallet.Api.Models;

public class IdempotencyRecord
{
    public long Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string RequestHash { get; set; } = string.Empty;

    public Guid TransferReference { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
