using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Company.Email.SendGrid;

public sealed class SendGridOptions
{
    /// <summary>API key starting with "SG.". Keep it in Key Vault / user secrets, not appsettings.json.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Change to https://api.eu.sendgrid.com for EU data residency.</summary>
    public string BaseUrl { get; set; } = "https://api.sendgrid.com";
}

/// <summary>Sends through SendGrid's v3 /mail/send REST endpoint.</summary>
public sealed class SendGridEmailProvider(string name, HttpClient http, SendGridOptions options) : IEmailProvider
{
    public string Name => name;

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            return EmailSendResult.Fail(Name, "SendGrid ApiKey is not configured.");

        var payload = await BuildPayloadAsync(message, ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl.TrimEnd('/')}/v3/mail/send")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new("Bearer", options.ApiKey);

        using var response = await http.SendAsync(request, ct);

        if (response.IsSuccessStatusCode) // SendGrid returns 202 Accepted
        {
            response.Headers.TryGetValues("X-Message-Id", out var ids);
            return EmailSendResult.Ok(Name, ids?.FirstOrDefault());
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var transient = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
        return EmailSendResult.Fail(Name, $"SendGrid returned {(int)response.StatusCode}: {body}", transient);
    }

    internal static async Task<SgMail> BuildPayloadAsync(EmailMessage m, CancellationToken ct)
    {
        // SendGrid rejects empty arrays, so optional lists become null and are left out of the JSON.
        static List<SgAddress>? Map(IReadOnlyList<EmailAddress> list) =>
            list.Count == 0 ? null : list.Select(a => new SgAddress(a.Address, a.DisplayName)).ToList();

        // text/plain must come before text/html.
        var content = new List<SgContent>();
        if (!string.IsNullOrEmpty(m.TextBody)) content.Add(new("text/plain", m.TextBody));
        if (!string.IsNullOrEmpty(m.HtmlBody)) content.Add(new("text/html", m.HtmlBody));

        List<SgAttachment>? attachments = null;
        if (m.Attachments.Count > 0)
        {
            attachments = [];
            foreach (var a in m.Attachments)
            {
                using var ms = new MemoryStream();
                await a.Content.CopyToAsync(ms, ct);
                attachments.Add(new(Convert.ToBase64String(ms.ToArray()), a.FileName, a.ContentType));
            }
        }

        return new SgMail(
            [new SgPersonalization(Map(m.To)!, Map(m.Cc), Map(m.Bcc))],
            new SgAddress(m.From.Address, m.From.DisplayName),
            m.Subject,
            content,
            attachments);
    }

    // SendGrid JSON shapes
    internal sealed record SgMail(
        [property: JsonPropertyName("personalizations")] List<SgPersonalization> Personalizations,
        [property: JsonPropertyName("from")] SgAddress From,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("content")] List<SgContent> Content,
        [property: JsonPropertyName("attachments"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<SgAttachment>? Attachments);

    internal sealed record SgPersonalization(
        [property: JsonPropertyName("to")] List<SgAddress> To,
        [property: JsonPropertyName("cc"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<SgAddress>? Cc,
        [property: JsonPropertyName("bcc"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<SgAddress>? Bcc);

    internal sealed record SgAddress(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name);

    internal sealed record SgContent(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("value")] string Value);

    internal sealed record SgAttachment(
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("filename")] string FileName,
        [property: JsonPropertyName("type")] string Type);
}

public static class SendGridExtensions
{
    /// <summary>Registers SendGrid. Bind from a config section such as "Email:SendGrid".</summary>
    public static EmailBuilder AddSendGrid(this EmailBuilder builder, IConfiguration section, string name = "SendGrid")
    {
        var options = section.Get<SendGridOptions>() ?? new SendGridOptions();
        builder.Services.AddHttpClient(name);

        return builder.AddProvider(name, sp =>
            new SendGridEmailProvider(name, sp.GetRequiredService<IHttpClientFactory>().CreateClient(name), options));
    }
}
