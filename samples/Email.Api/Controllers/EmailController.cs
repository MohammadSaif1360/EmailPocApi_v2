using Company.Email;
using Email.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Email.Api.Controllers;

/// <summary>
/// Thin HTTP layer over IEmailService. Note: this controller never mentions Azure, AWS or SendGrid —
/// which provider sends the email is decided only by Program.cs and appsettings.json.
/// </summary>
[ApiController]
[Route("api/email")]
[Produces("application/json")]
public sealed class EmailController(
    IEmailService email,                 // injected by DI: we never write `new EmailService(...)`
    EmailProviderRegistry registry,
    IOptions<EmailOptions> emailOptions,
    IConfiguration config,
    ILogger<EmailController> logger) : ControllerBase
{
    /// <summary>Shows which providers this app has registered and which one is the default.</summary>
    [HttpGet("providers")]
    public IActionResult GetProviders() => Ok(new
    {
        defaultProvider = emailOptions.Value.DefaultProvider,
        fallbackProviders = emailOptions.Value.FallbackProviders,
        registered = registry.Names
    });

    /// <summary>Sends an email (no attachments) from a JSON body.</summary>
    [HttpPost("send")]
    [ProducesResponseType<SendEmailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<SendEmailResponse>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Send([FromBody] SendEmailRequest request, CancellationToken ct)
    {
        try
        {
            var message = new EmailMessage
            {
                From = ResolveFrom(request.From),
                To = ToAddresses(request.To),
                Cc = ToAddresses(request.Cc),
                Bcc = ToAddresses(request.Bcc),
                Subject = request.Subject,
                HtmlBody = request.HtmlBody,
                TextBody = request.TextBody
            };

            return await SendAsync(message, request.Provider, ct);
        }
        catch (InvalidOperationException ex)
        {
            return ConfigurationProblem(ex);
        }
    }

    /// <summary>Sends an email with file attachments (multipart/form-data). Addresses are comma-separated.</summary>
    [HttpPost("send-with-attachments")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<SendEmailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<SendEmailResponse>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendWithAttachments([FromForm] SendEmailFormRequest request, CancellationToken ct)
    {
        var attachments = new List<EmailAttachment>();
        try
        {
            foreach (var file in request.Attachments)
            {
                // Copy each upload into memory so its size is known (for validation) and it can be read by the provider.
                var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, ct);
                buffer.Position = 0;
                var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
                attachments.Add(new EmailAttachment(file.FileName, contentType, buffer));
            }

            var message = new EmailMessage
            {
                From = ResolveFrom(request.From),
                To = ToAddresses(SplitList(request.To)),
                Cc = ToAddresses(SplitList(request.Cc)),
                Bcc = ToAddresses(SplitList(request.Bcc)),
                Subject = request.Subject,
                HtmlBody = request.HtmlBody,
                TextBody = request.TextBody,
                Attachments = attachments
            };

            return await SendAsync(message, request.Provider, ct);
        }
        catch (InvalidOperationException ex)
        {
            return ConfigurationProblem(ex);
        }
        finally
        {
            foreach (var a in attachments) await a.Content.DisposeAsync();
        }
    }

    // ───────── helpers ─────────

    private async Task<IActionResult> SendAsync(EmailMessage message, string? provider, CancellationToken ct)
    {
        // Caller mistakes (bad address, no body, too-large attachments) → 400, before any provider is called.
        var errors = EmailMessageValidator.Validate(message, emailOptions.Value);
        if (errors.Count > 0)
            return Problem(title: "Invalid email", detail: string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);

        var result = await email.SendAsync(message, string.IsNullOrWhiteSpace(provider) ? null : provider, ct);
        var response = new SendEmailResponse(result.Success, result.Provider, result.MessageId, result.Error);

        // 200 = provider accepted the email. 502 = the provider rejected it; the error says why.
        return result.Success ? Ok(response) : StatusCode(StatusCodes.Status502BadGateway, response);
    }

    /// <summary>Unknown provider name, missing From, or a provider whose settings are missing (e.g. no ACS connection string).</summary>
    private ObjectResult ConfigurationProblem(InvalidOperationException ex)
    {
        logger.LogWarning("Email configuration problem: {Message}", ex.Message);
        return Problem(title: "Email configuration problem", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
    }

    private EmailAddress ResolveFrom(string? from)
    {
        var address = string.IsNullOrWhiteSpace(from) ? config["EmailApi:DefaultFrom"] : from;
        if (string.IsNullOrWhiteSpace(address))
            throw new InvalidOperationException("No From address given and EmailApi:DefaultFrom is not configured.");

        return new EmailAddress(address, config["EmailApi:DefaultFromName"]);
    }

    private static List<EmailAddress> ToAddresses(IEnumerable<string> addresses) =>
        addresses.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => new EmailAddress(a.Trim())).ToList();

    private static IEnumerable<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
