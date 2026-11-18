namespace Company.Email;

/// <summary>An email address with an optional display name.</summary>
public sealed record EmailAddress(string Address, string? DisplayName = null)
{
    /// <summary>Lets you write "a@b.com" wherever an EmailAddress is expected.</summary>
    public static implicit operator EmailAddress(string address) => new(address);

    public override string ToString() =>
        string.IsNullOrWhiteSpace(DisplayName) ? Address : $"{DisplayName} <{Address}>";
}

/// <summary>A file attached to the email. Content is a stream so large files are not loaded into memory twice.</summary>
public sealed record EmailAttachment(string FileName, string ContentType, Stream Content)
{
    public static EmailAttachment FromFile(string path, string contentType = "application/octet-stream") =>
        new(Path.GetFileName(path), contentType, File.OpenRead(path));
}

/// <summary>
/// The provider-independent email. Apps fill this; providers translate it to their own API.
/// </summary>
public sealed class EmailMessage
{
    public required EmailAddress From { get; init; }
    public required IReadOnlyList<EmailAddress> To { get; init; }
    public IReadOnlyList<EmailAddress> Cc { get; init; } = [];
    public IReadOnlyList<EmailAddress> Bcc { get; init; } = [];
    public required string Subject { get; init; }

    /// <summary>HTML body. Set this, TextBody, or both.</summary>
    public string? HtmlBody { get; init; }

    /// <summary>Plain-text body for clients that don't render HTML.</summary>
    public string? TextBody { get; init; }

    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];
}
