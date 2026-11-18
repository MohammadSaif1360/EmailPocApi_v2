namespace Company.Email;

/// <summary>Bound from the "Email" section of appsettings.json.</summary>
public sealed class EmailOptions
{
    /// <summary>Provider used when the caller doesn't name one, e.g. "AzureAcs".</summary>
    public string DefaultProvider { get; set; } = "";

    /// <summary>
    /// Providers to try, in order, when the default provider fails.
    /// Only used when the caller did not name a provider explicitly.
    /// </summary>
    public List<string> FallbackProviders { get; set; } = [];

    public RetryOptions Retry { get; set; } = new();

    /// <summary>Total attachment size allowed per email. Set it to the strictest provider's limit.</summary>
    public long MaxTotalAttachmentBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Maximum To + Cc + Bcc recipients per email.</summary>
    public int MaxRecipients { get; set; } = 50;
}

public sealed class RetryOptions
{
    /// <summary>Total attempts per provider, including the first. 1 disables retry.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Delay before the first retry. Doubles on each further retry.</summary>
    public int BaseDelayMilliseconds { get; set; } = 500;
}
