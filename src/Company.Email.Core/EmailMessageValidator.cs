using System.Net.Mail;

namespace Company.Email;

/// <summary>Checks a message before any provider is called, so bad input fails fast and the same way for every provider.</summary>
public static class EmailMessageValidator
{
    public static IReadOnlyList<string> Validate(EmailMessage message, EmailOptions options)
    {
        var errors = new List<string>();

        CheckAddress(message.From, "From", errors);

        if (message.To.Count == 0)
            errors.Add("At least one To recipient is required.");

        foreach (var a in message.To) CheckAddress(a, "To", errors);
        foreach (var a in message.Cc) CheckAddress(a, "Cc", errors);
        foreach (var a in message.Bcc) CheckAddress(a, "Bcc", errors);

        var recipientCount = message.To.Count + message.Cc.Count + message.Bcc.Count;
        if (recipientCount > options.MaxRecipients)
            errors.Add($"{recipientCount} recipients exceeds the limit of {options.MaxRecipients}.");

        if (string.IsNullOrWhiteSpace(message.Subject))
            errors.Add("Subject is required.");

        if (string.IsNullOrWhiteSpace(message.HtmlBody) && string.IsNullOrWhiteSpace(message.TextBody))
            errors.Add("HtmlBody or TextBody is required.");

        long totalBytes = 0;
        foreach (var att in message.Attachments)
        {
            if (string.IsNullOrWhiteSpace(att.FileName))
                errors.Add("Every attachment needs a FileName.");
            if (att.Content.CanSeek)
                totalBytes += att.Content.Length;
        }
        if (totalBytes > options.MaxTotalAttachmentBytes)
            errors.Add($"Attachments total {totalBytes:N0} bytes, over the {options.MaxTotalAttachmentBytes:N0} byte limit.");

        return errors;
    }

    private static void CheckAddress(EmailAddress? address, string field, List<string> errors)
    {
        if (address is null || !MailAddress.TryCreate(address.Address, out _))
            errors.Add($"{field} address '{address?.Address}' is not a valid email address.");
    }
}
