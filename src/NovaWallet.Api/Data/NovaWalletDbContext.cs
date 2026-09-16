using Microsoft.EntityFrameworkCore;
using NovaWallet.Api.Models;

namespace NovaWallet.Api.Data;

public class NovaWalletDbContext : DbContext
{
    public NovaWalletDbContext(DbContextOptions<NovaWalletDbContext> options)
        : base(options)
    {
    }

    public DbSet<Wallet> Wallets => Set<Wallet>();

    public DbSet<LedgerTransaction> Transactions => Set<LedgerTransaction>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Wallet>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.CustomerId)
                .HasMaxLength(100)
                .IsRequired();

            entity.HasIndex(x => x.CustomerId)
                .IsUnique();

            entity.Property(x => x.Currency)
                .HasMaxLength(3)
                .IsRequired();

            entity.ToTable(t => t.HasCheckConstraint(
    "CK_Wallet_Balance_NonNegative",
    "\"BalanceKobo\" >= 0"));
        });

        modelBuilder.Entity<LedgerTransaction>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Type)
                .HasConversion<string>()
                .HasMaxLength(30);

            entity.HasIndex(x => new { x.WalletId, x.CreatedAt });

            entity.HasOne(x => x.Wallet)
                .WithMany()
                .HasForeignKey(x => x.WalletId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable(t => t.HasCheckConstraint(
    "CK_Transaction_Amount_Positive",
    "\"AmountKobo\" > 0"));
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Action)
                .HasMaxLength(50)
                .IsRequired();

            entity.HasIndex(x => new { x.WalletId, x.CreatedAt });
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Key)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.RequestHash)
                .HasMaxLength(64)
                .IsRequired();

            entity.HasIndex(x => x.Key)
                .IsUnique();
        });
    }
}
