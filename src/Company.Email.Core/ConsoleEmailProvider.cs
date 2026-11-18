namespace Company.Email;

/// <summary>Prints the email instead of sending it. Use in local development and tests.</summary>
public sealed class ConsoleEmailProvider(string name = "Console") : IEmailProvider
{
    public string Name => name;

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        Console.WriteLine($"""
            ┌─ [{Name}] email ───────────────────────────
            │ From:    {message.From}
            │ To:      {string.Join(", ", message.To)}
            │ Cc:      {string.Join(", ", message.Cc)}
            │ Bcc:     {string.Join(", ", message.Bcc)}
            │ Subject: {message.Subject}
            │ Body:    {message.TextBody ?? message.HtmlBody}
            │ Files:   {string.Join(", ", message.Attachments.Select(a => a.FileName))}
            └────────────────────────────────────────────
            """);

        return Task.FromResult(EmailSendResult.Ok(Name, $"console-{Guid.NewGuid():N}"[..16]));
    }
}
