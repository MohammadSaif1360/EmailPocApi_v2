using System.Security.Cryptography.X509Certificates;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using AppMessage = Company.Email.EmailMessage;

namespace Company.Email.Smtp;

public sealed class SmtpOptions
{
    /// <summary>e.g. smtp.sendgrid.net, email-smtp.ap-south-1.amazonaws.com, smtp.azurecomm.net</summary>
    public string Host { get; set; } = "";

    /// <summary>587 (STARTTLS) is the usual choice; 465 for implicit TLS.</summary>
    public int Port { get; set; } = 587;

    public string? UserName { get; set; }
    public string? Password { get; set; }

    /// <summary>Auto, StartTls, SslOnConnect or None.</summary>
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.StartTls;

    /// <summary>Optional client certificate (.pfx) for servers that require certificate authentication.</summary>
    public string? ClientCertificatePath { get; set; }
    public string? ClientCertificatePassword { get; set; }
}

/// <summary>Universal provider: anything that speaks SMTP. No provider-specific features (tracking, templates).</summary>
public sealed class SmtpEmailProvider(string name, SmtpOptions options) : IEmailProvider
{
    public string Name => name;

    public async Task<EmailSendResult> SendAsync(AppMessage m, CancellationToken ct = default)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(m.From.DisplayName, m.From.Address));
        foreach (var a in m.To) mime.To.Add(new MailboxAddress(a.DisplayName, a.Address));
        foreach (var a in m.Cc) mime.Cc.Add(new MailboxAddress(a.DisplayName, a.Address));
        foreach (var a in m.Bcc) mime.Bcc.Add(new MailboxAddress(a.DisplayName, a.Address));
        mime.Subject = m.Subject;

        var body = new BodyBuilder { HtmlBody = m.HtmlBody, TextBody = m.TextBody };
        foreach (var att in m.Attachments)
            body.Attachments.Add(att.FileName, att.Content, ContentType.Parse(att.ContentType));
        mime.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            if (!string.IsNullOrWhiteSpace(options.ClientCertificatePath))
            {
                client.ClientCertificates = new X509CertificateCollection
                {
                    new X509Certificate2(options.ClientCertificatePath, options.ClientCertificatePassword)
                };
            }

            await client.ConnectAsync(options.Host, options.Port, options.Security, ct);
            if (!string.IsNullOrWhiteSpace(options.UserName))
                await client.AuthenticateAsync(options.UserName, options.Password, ct);

            var serverResponse = await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);
            return EmailSendResult.Ok(Name, mime.MessageId ?? serverResponse);
        }
        catch (SmtpCommandException ex)
        {
            // 4xx SMTP codes are temporary (try again later), 5xx are permanent.
            var transient = (int)ex.StatusCode is >= 400 and < 500;
            return EmailSendResult.Fail(Name, $"SMTP {(int)ex.StatusCode}: {ex.Message}", transient);
        }
        catch (Exception ex) when (ex is ServiceNotConnectedException or SmtpProtocolException or IOException)
        {
            return EmailSendResult.Fail(Name, ex.Message, isTransient: true);
        }
    }
}

public static class SmtpExtensions
{
    /// <summary>Registers an SMTP provider. Bind from a config section such as "Email:Smtp".</summary>
    public static EmailBuilder AddSmtp(this EmailBuilder builder, IConfiguration section, string name = "Smtp")
    {
        var options = section.Get<SmtpOptions>() ?? new SmtpOptions();
        return builder.AddProvider(name, _ => new SmtpEmailProvider(name, options));
    }
}
