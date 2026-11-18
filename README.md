# Reusable Email Notification POC (.NET 8)

One library any .NET project can use to send email through **Azure Communication Services, Amazon SES, Twilio SendGrid or any SMTP server**, switchable by configuration. New providers plug in without changing existing code.

## Real-world sample: Email.Api (Web API + Swagger)

`samples/Email.Api` is an ASP.NET Core Web API that uses the library to send real email through **Azure Communication Services**, tested from a Swagger page. Set it as the startup project and press F5.
Full setup, from Azure portal to first email: **[samples/Email.Api/README-AzureACS.md](samples/Email.Api/README-AzureACS.md)**.

| Endpoint | Purpose |
|---|---|
| `GET /api/email/providers` | Which providers are registered and which is the default |
| `POST /api/email/send` | Send an email from a JSON body (to, cc, bcc, subject, htmlBody, textBody, provider) |
| `POST /api/email/send-with-attachments` | Same, as a form with file uploads |

## Quick start (console demos)

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download) or later.

```bash
cd EmailPoc
dotnet run --project samples/Email.Demo
```

This runs 7 demos with fake providers, so no email account is needed.

| # | Demo | Approach shown |
|---|---|---|
| 1 | Send via the default provider from `appsettings.json` | 2 – DI keyed services |
| 2 | Provider fails twice with 429, succeeds on the 3rd try | 4 – Retry decorator |
| 3 | Primary provider is down, the next one takes over | 4 – Fallback decorator |
| 4 | Bad address and missing body rejected before any provider is called | Validation |
| 5 | CC, BCC, display names and an attachment | All parameters |
| 6 | The same send through a `switch` factory | 1 – Strategy + Factory (comparison only) |
| 7 | A provider loaded from a DLL the app never referenced | 3 – Runtime plugin |

To enable demo 7, build the plugin into the demo's `plugins` folder, then run again:

```bash
dotnet build samples/Company.Email.FileDropPlugin -o samples/Email.Demo/bin/Debug/net8.0/plugins
dotnet run --project samples/Email.Demo
```

## Sending a real email

1. Set the sender and recipient in `samples/Email.Demo/appsettings.json` under `Email:Test`. The sender must be verified with the provider.
2. Store the provider's credentials as user secrets so they never get committed:

   ```bash
   cd samples/Email.Demo
   dotnet user-secrets set "Email:SendGrid:ApiKey" "SG.xxxxx"
   dotnet user-secrets set "Email:AzureAcs:ConnectionString" "endpoint=https://...;accesskey=..."
   dotnet user-secrets set "Email:AwsSes:AccessKey" "AKIA..."
   dotnet user-secrets set "Email:AwsSes:SecretKey" "..."
   dotnet user-secrets set "Email:Smtp:Password" "..."
   ```

3. Send:

   ```bash
   dotnet run -- send SendGrid      # or AzureAcs, AwsSes, Smtp
   ```

Provider setup notes:
- **SendGrid:** create an API key with *Mail Send* permission and verify a sender or domain.
- **Azure ACS:** create a Communication Services resource and an Email Communication Service, connect a verified domain, and use the `MailFrom` address of that domain as the sender.
- **AWS SES:** verify the sender identity. New accounts are in the sandbox, so recipients must be verified too until production access is granted.
- **SMTP:** works with any SMTP server, including the SMTP endpoints of all three providers above.

## Solution layout

```
src/
  Company.Email.Abstractions   EmailMessage, IEmailProvider, IEmailService (no dependencies)
  Company.Email.Core           EmailService, validation, retry/fallback/logging decorators,
                               DI registration, plugin loader, Console provider
  Company.Email.SendGrid       SendGrid v3 REST API via HttpClient (no SDK)
  Company.Email.AzureAcs       Azure.Communication.Email SDK
  Company.Email.AwsSes         AWSSDK.SimpleEmailV2 (+ MimeKit for attachments)
  Company.Email.Smtp           MailKit; host/port/user/password, optional client certificate
samples/
  Email.Api                    ASP.NET Core Web API + Swagger, real sending via Azure ACS
  Email.Demo                   console app running all demos
  Company.Email.FileDropPlugin example runtime plugin (writes emails to ./outbox)
```

Each provider is its own project, so an app that only uses SendGrid never pulls in the AWS or Azure SDKs.

## Using it in another project

1. Reference `Company.Email.Core` and the provider project(s) you need. Later, as NuGet packages from your Azure DevOps Artifacts feed: `dotnet pack -c Release`, then push.
2. Add configuration:

   ```json
   "Email": {
     "DefaultProvider": "AzureAcs",
     "FallbackProviders": [ "AwsSes" ],
     "AzureAcs": { "ConnectionString": "" },
     "AwsSes": { "Region": "me-central-1" }
   }
   ```

3. Register in `Program.cs`:

   ```csharp
   builder.Services.AddEmailNotifications(builder.Configuration.GetSection("Email"))
       .AddAzureAcs(builder.Configuration.GetSection("Email:AzureAcs"))
       .AddAwsSes(builder.Configuration.GetSection("Email:AwsSes"));
   ```

4. Inject and send:

   ```csharp
   public class InvoiceService(IEmailService email)
   {
       public Task NotifyAsync(string customerEmail) => email.SendAsync(new EmailMessage
       {
           From = "noreply@company.com",
           To = [customerEmail],
           Subject = "Your invoice",
           HtmlBody = "<p>Invoice attached.</p>"
       });
   }
   ```

## Adding a new provider (e.g. Mailgun)

1. Create `Company.Email.Mailgun` with a class implementing `IEmailProvider`.
2. Add an extension method `AddMailgun(this EmailBuilder builder, IConfiguration section)` that calls `builder.AddProvider("Mailgun", ...)`.
3. Apps call `.AddMailgun(...)`.

No existing project changes. That is the Open/Closed principle the POC demonstrates.

## How a send flows

```
IEmailService.SendAsync(message, provider?)
  ├─ validate message (addresses, body, recipient count, attachment size)
  ├─ pick provider: explicit name, or DefaultProvider + FallbackProviders
  └─ for each provider in the chain:
       LoggingEmailProvider → RetryEmailProvider → actual provider (resolved by name from DI)
       stop at the first success
```

- **Retry** happens only for transient failures: HTTP 429, HTTP 5xx, SMTP 4xx, and network errors. Delays are 0.5 s, 1 s, 2 s, and so on.
- **Fallback** applies only when the caller did not name a provider. An explicit choice is respected.
- **Success** means the provider *accepted* the email. Delivery and bounces are reported asynchronously by each provider (webhooks or Event Grid), which is a future phase.

## Approaches evaluated

| Approach | Used in POC | Notes |
|---|---|---|
| 1. Strategy + Factory (`switch`) | Comparison only | Simple, but every new provider means editing the factory |
| 2. DI keyed services (provider pattern) | **Core design** | Idiomatic .NET 8, config-driven, no central list |
| 3. Runtime plugins (`AssemblyLoadContext`) | Demo | No recompile, but version conflicts and security risk; use only if "no redeploy" is a hard requirement |
| 4. Decorators (retry, fallback, logging) | **Yes** | Cross-cutting behaviour without touching providers |
| 5. Generic SMTP | **Yes** | Universal fallback; loses provider-native features |
| 6. Queue-based notification service | Roadmap | Service Bus or SQS plus a worker using this library; for high volume and multi-app setups |

## Verification status

The Abstractions, Core and SendGrid projects, the console demo, the plugin and the Web API (with the Console provider) were compiled and run on .NET 8. The API's endpoints were exercised with JSON and multipart requests. The JSON sent to SendGrid was checked against its v3 API shape.

The Azure ACS, AWS SES and SMTP providers could not be compiled in the build environment because their SDK packages were unavailable offline. Run `dotnet build` once on your machine to confirm them.

Package versions for AWS (`3.7.*`) and MailKit/MimeKit (`4.*`) float to the latest minor release; pin exact versions before production.
