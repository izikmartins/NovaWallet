namespace NovaWallet.Api.Exceptions;

public class WalletNotFoundException : Exception
{
    public WalletNotFoundException(Guid walletId)
        : base($"Wallet '{walletId}' was not found.")
    {
    }
}

public class InsufficientFundsException : Exception
{
    public InsufficientFundsException()
        : base("The source wallet has insufficient funds.")
    {
    }
}

public class DailyLimitExceededException : Exception
{
    public DailyLimitExceededException()
        : base("The daily outbound transfer limit has been exceeded.")
    {
    }
}

public class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException()
        : base("The Idempotency-Key has already been used with a different request payload.")
    {
    }
}

public class InvalidTransferException : Exception
{
    public InvalidTransferException(string message) : base(message)
    {
    }
}
