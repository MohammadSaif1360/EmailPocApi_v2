using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Configuration;
// Azure's SDK has its own EmailMessage/EmailAddress/EmailAttachment types, so alias ours and theirs.
using AcsAddress = Azure.Communication.Email.EmailAddress;
using AcsAttachment = Azure.Communication.Email.EmailAttachment;
using AcsMessage = Azure.Communication.Email.EmailMessage;
using AppAddress = Company.Email.EmailAddress;
using AppMessage = Company.Email.EmailMessage;

namespace Company.Email.AzureAcs;

public sealed class AzureAcsOptions
{
    /// <summary>"endpoint=https://xxx.communication.azure.com/;accesskey=..." from the ACS resource's Keys blade.</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// false (default): return as soon as ACS ACCEPTS the email — fastest, best for production.
    /// true: wait until ACS reports the final status (Succeeded/Failed) — slower (a few seconds),
    /// but handy while testing because a bad recipient or sender shows up as a failure immediately.
    /// </summary>
    public bool WaitForCompletion { get; set; }
}

/// <summary>
/// Sends through Azure Communication Services Email.
/// ACS sends are long-running operations. By default this returns once ACS has ACCEPTED the email
/// (WaitUntil.Started); delivery status arrives later via Event Grid or by polling the operation ID.
/// Set WaitForCompletion to wait for the final status instead.
/// </summary>
public sealed class AzureAcsEmailProvider(string name, EmailClient client, bool waitForCompletion = false) : IEmailProvider
{
    public string Name => name;

    public async Task<EmailSendResult> SendAsync(AppMessage m, CancellationToken ct = default)
    {
        var content = new EmailContent(m.Subject) { Html = m.HtmlBody, PlainText = m.TextBody };

        var recipients = new EmailRecipients(Map(m.To), Map(m.Cc), Map(m.Bcc));

        // ACS has no separate "from display name": the sender must be a MailFrom address of a verified domain.
        var acsMessage = new AcsMessage(m.From.Address, recipients, content);

        foreach (var a in m.Attachments)
            acsMessage.Attachments.Add(new AcsAttachment(a.FileName, a.ContentType, await BinaryData.FromStreamAsync(a.Content, ct)));

        try
        {
            if (!waitForCompletion)
            {
                EmailSendOperation started = await client.SendAsync(WaitUntil.Started, acsMessage, ct);
                return EmailSendResult.Ok(Name, started.Id);
            }

            // Polls ACS until the send finishes. A failed send normally throws RequestFailedException (caught below).
            EmailSendOperation completed = await client.SendAsync(WaitUntil.Completed, acsMessage, ct);
            return completed.Value.Status == EmailSendStatus.Succeeded
                ? EmailSendResult.Ok(Name, completed.Id)
                : EmailSendResult.Fail(Name, $"ACS finished with status {completed.Value.Status} (operation {completed.Id}).");
        }
        catch (RequestFailedException ex)
        {
            var transient = ex.Status == 429 || ex.Status >= 500;
            return EmailSendResult.Fail(Name, $"ACS {ex.Status} {ex.ErrorCode}: {ex.Message}", transient);
        }
    }

    private static List<AcsAddress> Map(IReadOnlyList<AppAddress> list) =>
        list.Select(a => new AcsAddress(a.Address, a.DisplayName)).ToList();
}

public static class AzureAcsExtensions
{
    /// <summary>Registers Azure ACS. Bind from a config section such as "Email:AzureAcs".</summary>
    public static EmailBuilder AddAzureAcs(this EmailBuilder builder, IConfiguration section, string name = "AzureAcs")
    {
        var options = section.Get<AzureAcsOptions>() ?? new AzureAcsOptions();

        // The client is created on first use, so a missing connection string only fails if ACS is actually used.
        return builder.AddProvider(name, _ =>
        {
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
                throw new InvalidOperationException($"{name}: ConnectionString is not configured.");
            return new AzureAcsEmailProvider(name, new EmailClient(options.ConnectionString), options.WaitForCompletion);
        });
    }
}
