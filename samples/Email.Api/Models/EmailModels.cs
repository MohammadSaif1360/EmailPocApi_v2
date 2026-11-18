using System.ComponentModel.DataAnnotations;

namespace Email.Api.Models;

/// <summary>JSON body for POST /api/email/send.</summary>
public sealed class SendEmailRequest
{
    /// <summary>Sender. Leave empty to use EmailApi:DefaultFrom from appsettings.json (for ACS: your verified MailFrom address).</summary>
    [EmailAddress]
    public string? From { get; set; }

    /// <example>["someone@example.com"]</example>
    [Required, MinLength(1)]
    public List<string> To { get; set; } = [];

    public List<string> Cc { get; set; } = [];
    public List<string> Bcc { get; set; } = [];

    /// <example>Test email from the Email API</example>
    [Required]
    public string Subject { get; set; } = "";

    /// <example>&lt;h2&gt;Hello!&lt;/h2&gt;&lt;p&gt;Sent through the reusable email library.&lt;/p&gt;</example>
    public string? HtmlBody { get; set; }

    public string? TextBody { get; set; }

    /// <summary>Optional provider name (AzureAcs, Smtp, Console). Empty = DefaultProvider from configuration.</summary>
    public string? Provider { get; set; }
}

/// <summary>multipart/form-data body for POST /api/email/send-with-attachments.</summary>
public sealed class SendEmailFormRequest
{
    [EmailAddress]
    public string? From { get; set; }

    /// <summary>One or more addresses, separated by commas.</summary>
    [Required]
    public string To { get; set; } = "";

    /// <summary>Optional, comma-separated.</summary>
    public string? Cc { get; set; }

    /// <summary>Optional, comma-separated.</summary>
    public string? Bcc { get; set; }

    [Required]
    public string Subject { get; set; } = "";

    public string? HtmlBody { get; set; }
    public string? TextBody { get; set; }
    public string? Provider { get; set; }

    public List<IFormFile> Attachments { get; set; } = [];
}

/// <summary>What the API returns for every send.</summary>
public sealed record SendEmailResponse(bool Success, string Provider, string? MessageId, string? Error);
