using System.ComponentModel.DataAnnotations;

namespace NovaWallet.Api.DTOs;

public class CreateWalletRequest
{
    [Required]
    [MaxLength(100)]
    public string CustomerId { get; set; } = string.Empty;
}

public class CreditWalletRequest
{
    [Range(1, long.MaxValue)]
    public long AmountKobo { get; set; }

    [MaxLength(200)]
    public string? Description { get; set; }
}

public class TransferRequest
{
    public Guid FromWalletId { get; set; }

    public Guid ToWalletId { get; set; }

    [Range(1, long.MaxValue)]
    public long AmountKobo { get; set; }

    [MaxLength(200)]
    public string? Description { get; set; }
}
