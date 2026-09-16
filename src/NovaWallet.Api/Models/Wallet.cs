namespace NovaWallet.Api.Models;

public class Wallet
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string CustomerId { get; set; } = string.Empty;

    public long BalanceKobo { get; set; } = 0;

    public string Currency { get; set; } = "NGN";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
