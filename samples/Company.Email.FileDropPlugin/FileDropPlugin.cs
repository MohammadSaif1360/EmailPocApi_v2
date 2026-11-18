using Microsoft.Extensions.Configuration;

namespace Company.Email.FileDropPlugin;

/// <summary>Found by PluginLoader at runtime; registers the FileDrop provider.</summary>
public sealed class FileDropPlugin : IEmailProviderPlugin
{
    public void Register(EmailBuilder builder, IConfiguration emailSection)
    {
        var folder = emailSection["FileDrop:Folder"] ?? "outbox";
        builder.AddProvider("FileDrop", _ => new FileDropEmailProvider(folder));
    }
}

/// <summary>Saves each email as a text file. Handy for local testing without any email account.</summary>
public sealed class FileDropEmailProvider(string folder) : IEmailProvider
{
    public string Name => "FileDrop";

    public async Task<EmailSendResult> SendAsync(EmailMessage m, CancellationToken ct = default)
    {
        Directory.CreateDirectory(folder);
        var id = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24];
        var path = Path.Combine(folder, $"{id}.txt");

        var text = $"""
            From: {m.From}
            To: {string.Join(", ", m.To)}
            Cc: {string.Join(", ", m.Cc)}
            Bcc: {string.Join(", ", m.Bcc)}
            Subject: {m.Subject}
            Attachments: {string.Join(", ", m.Attachments.Select(a => a.FileName))}

            {m.TextBody ?? m.HtmlBody}
            """;

        await File.WriteAllTextAsync(path, text, ct);
        Console.WriteLine($"[FileDrop] saved {Path.GetFullPath(path)}");
        return EmailSendResult.Ok(Name, id);
    }
}
