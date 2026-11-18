using Company.Email;

namespace Email.Demo;

/// <summary>Fails with a transient error the first N times, then succeeds. Shows the retry decorator working.</summary>
public sealed class FlakyEmailProvider(int failuresBeforeSuccess) : IEmailProvider
{
    private int _calls;
    public string Name => "Flaky";

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        _calls++;
        if (_calls <= failuresBeforeSuccess)
            return Task.FromResult(EmailSendResult.Fail(Name, $"429 Too Many Requests (simulated, call {_calls})", isTransient: true));

        Console.WriteLine($"[Flaky] '{message.Subject}' accepted on call {_calls}");
        return Task.FromResult(EmailSendResult.Ok(Name, $"flaky-{_calls}"));
    }
}

/// <summary>Always fails permanently. Shows the fallback decorator moving to the next provider.</summary>
public sealed class DownEmailProvider : IEmailProvider
{
    public string Name => "Down";

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default) =>
        Task.FromResult(EmailSendResult.Fail(Name, "Service unavailable (simulated outage)", isTransient: false));
}
