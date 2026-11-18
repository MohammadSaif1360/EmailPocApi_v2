using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Company.Email;

// Decorators wrap a provider and add behaviour around it without changing it.

/// <summary>Retries transient failures with exponential backoff (500 ms, 1 s, 2 s...).</summary>
public sealed class RetryEmailProvider(IEmailProvider inner, RetryOptions options, ILogger logger) : IEmailProvider
{
    public string Name => inner.Name;

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var attempts = Math.Max(1, options.MaxAttempts);
        EmailSendResult result = EmailSendResult.Fail(Name, "Not attempted.");

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                result = await inner.SendAsync(message, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Unexpected exceptions (network drops, timeouts) are treated as transient.
                result = EmailSendResult.Fail(Name, ex.Message, isTransient: true);
            }

            if (result.Success || !result.IsTransient || attempt == attempts)
                return result;

            var delay = TimeSpan.FromMilliseconds(options.BaseDelayMilliseconds * Math.Pow(2, attempt - 1));
            logger.LogWarning("{Provider} attempt {Attempt}/{Max} failed: {Error}. Retrying in {Delay} ms.",
                Name, attempt, attempts, result.Error, delay.TotalMilliseconds);
            await Task.Delay(delay, ct);
        }

        return result;
    }
}

/// <summary>Tries each provider in order until one succeeds. E.g. Azure ACS → AWS SES → SMTP.</summary>
public sealed class FallbackEmailProvider(IReadOnlyList<IEmailProvider> providers, ILogger logger) : IEmailProvider
{
    public string Name => providers[0].Name;

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        EmailSendResult? last = null;

        foreach (var provider in providers)
        {
            last = await provider.SendAsync(message, ct);
            if (last.Success)
                return last;

            logger.LogWarning("{Provider} failed: {Error}. Trying next provider.", provider.Name, last.Error);
        }

        return last!;
    }
}

/// <summary>Logs every send with its duration and outcome.</summary>
public sealed class LoggingEmailProvider(IEmailProvider inner, ILogger logger) : IEmailProvider
{
    public string Name => inner.Name;

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = await inner.SendAsync(message, ct);

        if (result.Success)
            logger.LogInformation("Sent '{Subject}' via {Provider} in {Ms} ms (id: {MessageId}).",
                message.Subject, Name, sw.ElapsedMilliseconds, result.MessageId);
        else
            logger.LogError("Failed to send '{Subject}' via {Provider} after {Ms} ms: {Error}",
                message.Subject, Name, sw.ElapsedMilliseconds, result.Error);

        return result;
    }
}
