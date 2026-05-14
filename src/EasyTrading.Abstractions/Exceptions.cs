namespace EasyTrading.Abstractions;

/// <summary>Base exception for any exchange-side error reported by an EasyTrading client.</summary>
public class ExchangeApiException : Exception
{
    /// <summary>Creates a new <see cref="ExchangeApiException"/>.</summary>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="errorCode">Exchange-specific error code, if any.</param>
    /// <param name="innerException">Underlying exception, if any.</param>
    public ExchangeApiException(string message, string? errorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Exchange-specific error code if the venue provided one.</summary>
    public string? ErrorCode { get; }
}

/// <summary>The request was throttled or the account's rate-limit budget is exhausted.</summary>
public sealed class RateLimitException : ExchangeApiException
{
    /// <summary>Creates a new <see cref="RateLimitException"/>.</summary>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="retryAfter">Suggested delay before retrying.</param>
    public RateLimitException(string message, TimeSpan? retryAfter = null)
        : base(message)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>Suggested delay before retrying, if provided by the exchange.</summary>
    public TimeSpan? RetryAfter { get; }
}

/// <summary>The account has insufficient collateral / balance to execute the requested action.</summary>
public sealed class InsufficientFundsException : ExchangeApiException
{
    /// <summary>Creates a new <see cref="InsufficientFundsException"/>.</summary>
    /// <param name="message">Human-readable error message.</param>
    public InsufficientFundsException(string message) : base(message) { }
}

/// <summary>The order parameters violate the market's constraints (tick size, min size, leverage, etc.).</summary>
public sealed class InvalidOrderException : ExchangeApiException
{
    /// <summary>Creates a new <see cref="InvalidOrderException"/>.</summary>
    /// <param name="message">Human-readable error message.</param>
    public InvalidOrderException(string message) : base(message) { }
}

/// <summary>Authentication failed — wrong credentials, expired agent, or missing approval.</summary>
public sealed class AuthenticationException : ExchangeApiException
{
    /// <summary>Creates a new <see cref="AuthenticationException"/>.</summary>
    /// <param name="message">Human-readable error message.</param>
    public AuthenticationException(string message) : base(message) { }
}

/// <summary>The request payload could not be signed (key error, encoding error, etc.).</summary>
public sealed class SigningException : ExchangeApiException
{
    /// <summary>Creates a new <see cref="SigningException"/>.</summary>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="innerException">Underlying exception, if any.</param>
    public SigningException(string message, Exception? innerException = null)
        : base(message, innerException: innerException) { }
}
