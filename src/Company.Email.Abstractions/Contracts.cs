namespace Company.Email;

/// <summary>Outcome of one send attempt.</summary>
/// <param name="Success">True when the provider accepted the email.</param>
/// <param name="Provider">Name of the provider that handled the request.</param>
/// <param name="MessageId">The provider's own ID for the email, useful for tracking.</param>
/// <param name="Error">Error text when Success is false.</param>
/// <param name="IsTransient">True for temporary failures (throttling, timeouts) that are worth retrying.</param>
public sealed record EmailSendResult(
    bool Success,
    string Provider,
    string? MessageId = null,
    string? Error = null,
    bool IsTransient = false)
{
    public static EmailSendResult Ok(string provider, string? messageId = null) =>
        new(true, provider, messageId);

    public static EmailSendResult Fail(string provider, string error, bool isTransient = false) =>
        new(false, provider, null, error, isTransient);
}

/// <summary>
/// The rule every provider must follow. One implementation per email service
/// (SendGrid, AWS SES, Azure ACS, SMTP...). Like an X++ interface.
/// </summary>
public interface IEmailProvider
{
    /// <summary>Unique name used to select this provider, e.g. "SendGrid".</summary>
    string Name { get; }

    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// What application code injects and calls. It picks the provider, validates,
/// and applies retry and fallback, so apps never touch providers directly.
/// </summary>
public interface IEmailService
{
    /// <param name="message">The email to send.</param>
    /// <param name="provider">Provider name. Null uses the configured default.</param>
    Task<EmailSendResult> SendAsync(EmailMessage message, string? provider = null, CancellationToken cancellationToken = default);
}
