using Amazon;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Configuration;
using MimeKit;
using AppMessage = Company.Email.EmailMessage;

namespace Company.Email.AwsSes;

public sealed class AwsSesOptions
{
    /// <summary>AWS region where SES is set up, e.g. "ap-south-1" (Mumbai) or "me-central-1" (UAE).</summary>
    public string Region { get; set; } = "ap-south-1";

    /// <summary>
    /// Leave AccessKey and SecretKey empty to use the default AWS credential chain
    /// (IAM role on EC2/ECS/Lambda, environment variables, ~/.aws/credentials). Preferred in production.
    /// </summary>
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
}

/// <summary>
/// Sends through Amazon SES v2. New SES accounts start in a sandbox where you can only
/// send to verified addresses until AWS grants production access.
/// </summary>
public sealed class AwsSesEmailProvider(string name, IAmazonSimpleEmailServiceV2 client) : IEmailProvider
{
    public string Name => name;

    public async Task<EmailSendResult> SendAsync(AppMessage m, CancellationToken ct = default)
    {
        var request = new SendEmailRequest
        {
            FromEmailAddress = m.From.ToString(),
            Destination = new Destination
            {
                ToAddresses = m.To.Select(a => a.ToString()).ToList(),
                CcAddresses = m.Cc.Select(a => a.ToString()).ToList(),
                BccAddresses = m.Bcc.Select(a => a.ToString()).ToList()
            },
            // Simple content has no attachments, so emails with attachments are sent as raw MIME.
            Content = m.Attachments.Count == 0 ? BuildSimple(m) : await BuildRawAsync(m, ct)
        };

        try
        {
            var response = await client.SendEmailAsync(request, ct);
            return EmailSendResult.Ok(Name, response.MessageId);
        }
        catch (TooManyRequestsException ex)
        {
            return EmailSendResult.Fail(Name, ex.Message, isTransient: true);
        }
        catch (AmazonSimpleEmailServiceV2Exception ex)
        {
            var transient = (int)ex.StatusCode >= 500;
            return EmailSendResult.Fail(Name, $"SES {(int)ex.StatusCode} {ex.ErrorCode}: {ex.Message}", transient);
        }
    }

    private static EmailContent BuildSimple(AppMessage m) => new()
    {
        Simple = new Message
        {
            Subject = new Content { Data = m.Subject, Charset = "UTF-8" },
            Body = new Body
            {
                Html = m.HtmlBody is null ? null : new Content { Data = m.HtmlBody, Charset = "UTF-8" },
                Text = m.TextBody is null ? null : new Content { Data = m.TextBody, Charset = "UTF-8" }
            }
        }
    };

    private static async Task<EmailContent> BuildRawAsync(AppMessage m, CancellationToken ct)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(m.From.DisplayName, m.From.Address));
        foreach (var a in m.To) mime.To.Add(new MailboxAddress(a.DisplayName, a.Address));
        foreach (var a in m.Cc) mime.Cc.Add(new MailboxAddress(a.DisplayName, a.Address));
        // Bcc goes in Destination only, never in the MIME headers.
        mime.Subject = m.Subject;

        var body = new BodyBuilder { HtmlBody = m.HtmlBody, TextBody = m.TextBody };
        foreach (var att in m.Attachments)
            body.Attachments.Add(att.FileName, att.Content, ContentType.Parse(att.ContentType));
        mime.Body = body.ToMessageBody();

        var stream = new MemoryStream();
        await mime.WriteToAsync(stream, ct);
        stream.Position = 0;

        return new EmailContent { Raw = new RawMessage { Data = stream } };
    }
}

public static class AwsSesExtensions
{
    /// <summary>Registers Amazon SES. Bind from a config section such as "Email:AwsSes".</summary>
    public static EmailBuilder AddAwsSes(this EmailBuilder builder, IConfiguration section, string name = "AwsSes")
    {
        var options = section.Get<AwsSesOptions>() ?? new AwsSesOptions();

        return builder.AddProvider(name, _ =>
        {
            var region = RegionEndpoint.GetBySystemName(options.Region);
            IAmazonSimpleEmailServiceV2 client = string.IsNullOrWhiteSpace(options.AccessKey)
                ? new AmazonSimpleEmailServiceV2Client(region)
                : new AmazonSimpleEmailServiceV2Client(options.AccessKey, options.SecretKey, region);
            return new AwsSesEmailProvider(name, client);
        });
    }
}
