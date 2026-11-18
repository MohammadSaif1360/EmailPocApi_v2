using System.Text;
using Company.Email;
using Company.Email.AwsSes;
using Company.Email.AzureAcs;
using Company.Email.SendGrid;
using Company.Email.Smtp;
using Email.Demo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Usage:
//   dotnet run                      -> runs every demo with fake providers (no accounts needed)
//   dotnet run -- send SendGrid     -> sends a REAL test email via SendGrid (or AzureAcs, AwsSes, Smtp)

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>(optional: true)   // keep real keys here: dotnet user-secrets set "Email:SendGrid:ApiKey" "SG.xxx"
    .AddEnvironmentVariables()                 // or here: Email__SendGrid__ApiKey=SG.xxx
    .Build();

var emailSection = config.GetSection("Email");

if (args is ["send", var providerName])
{
    await SendRealAsync(providerName);
    return;
}

// ── Demo 1: Approach 2 — default provider from appsettings.json ──
Title("1. Default provider (DI keyed services)");
var email = BuildServices().GetRequiredService<IEmailService>();
Print(await email.SendAsync(Sample("Demo 1: default provider")));

// ── Demo 2: Approach 2 + 4 — explicit provider, retry decorator ──
Title("2. Explicit provider + retry (fails twice, succeeds on 3rd attempt)");
Print(await email.SendAsync(Sample("Demo 2: retry"), provider: "Flaky"));

// ── Demo 3: Approach 4 — fallback chain ──
Title("3. Fallback: 'Down' fails, so 'Console' takes over");
var withFallback = BuildServices(o => { o.DefaultProvider = "Down"; o.FallbackProviders = ["Console"]; })
    .GetRequiredService<IEmailService>();
Print(await withFallback.SendAsync(Sample("Demo 3: fallback")));

// ── Demo 4: validation runs before any provider is called ──
Title("4. Validation (bad address, no body)");
Print(await email.SendAsync(new EmailMessage
{
    From = "noreply@company.com",
    To = ["not-an-email"],
    Subject = "Demo 4: invalid"
}));

// ── Demo 5: attachments, CC, BCC, display names ──
Title("5. All parameters: CC, BCC, display names, attachment");
var report = new MemoryStream(Encoding.UTF8.GetBytes("Invoice,Amount\nINV-001,1200\n"));
Print(await email.SendAsync(new EmailMessage
{
    From = new EmailAddress("noreply@company.com", "TechnoRUCS Notifications"),
    To = [new EmailAddress("saif@company.com", "Saif")],
    Cc = ["manager@company.com"],
    Bcc = ["audit@company.com"],
    Subject = "Demo 5: monthly report",
    HtmlBody = "<p>Please find the report attached.</p>",
    TextBody = "Please find the report attached.",
    Attachments = [new EmailAttachment("report.csv", "text/csv", report)]
}));

// ── Demo 6: Approach 1 for comparison ──
Title("6. Approach 1 (switch factory) — works, but needs editing for every new provider");
var fromFactory = Approach1_SwitchFactory.Create("Console");
Print(await fromFactory.SendAsync(Sample("Demo 6: switch factory")));

// ── Demo 7: Approach 3 — runtime plugin ──
Title("7. Runtime plugin (FileDrop loaded from ./plugins, not referenced by this app)");
var registry = BuildServices().GetRequiredService<EmailProviderRegistry>();
if (registry.Contains("FileDrop"))
    Print(await email.SendAsync(Sample("Demo 7: plugin"), provider: "FileDrop"));
else
    Console.WriteLine("FileDrop plugin not found. Build it into the plugins folder, then run again:\n" +
                      "  dotnet build samples/Company.Email.FileDropPlugin -o samples/Email.Demo/bin/Debug/net8.0/plugins");

Title("Registered providers");
Console.WriteLine(string.Join(", ", registry.Names));
return;


// ───────────────────────── helpers ─────────────────────────

// The ONE place an app wires email up. In ASP.NET Core this goes in Program.cs on builder.Services.
IServiceProvider BuildServices(Action<EmailOptions>? overrides = null)
{
    var services = new ServiceCollection();
    services.AddLogging(b => b
        .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
        .AddFilter("System.Net.Http", LogLevel.Warning)); // hide HttpClient's per-request noise

    services.AddEmailNotifications(emailSection)
        .AddSendGrid(emailSection.GetSection("SendGrid"))
        .AddAzureAcs(emailSection.GetSection("AzureAcs"))
        .AddAwsSes(emailSection.GetSection("AwsSes"))
        .AddSmtp(emailSection.GetSection("Smtp"))
        .AddConsoleProvider()
        .AddProvider("Flaky", _ => new FlakyEmailProvider(failuresBeforeSuccess: 2))
        .AddProvider("Down", _ => new DownEmailProvider())
        .AddPluginsFromFolder(Path.Combine(AppContext.BaseDirectory, "plugins"), emailSection);

    if (overrides is not null)
        services.PostConfigure(overrides);

    return services.BuildServiceProvider();
}

async Task SendRealAsync(string provider)
{
    var from = emailSection["Test:From"]!;
    var to = emailSection["Test:To"]!;
    Title($"Real send via {provider}: {from} -> {to}");

    var service = BuildServices(o => o.FallbackProviders = []).GetRequiredService<IEmailService>();
    var result = await service.SendAsync(new EmailMessage
    {
        From = from,
        To = [to],
        Subject = $"Email POC test via {provider}",
        HtmlBody = $"<h3>It works!</h3><p>Sent through <b>{provider}</b> at {DateTime.Now:F}.</p>",
        TextBody = $"It works! Sent through {provider} at {DateTime.Now:F}."
    }, provider);
    Print(result);
}

static EmailMessage Sample(string subject) => new()
{
    From = "noreply@company.com",
    To = ["saif@company.com"],
    Subject = subject,
    TextBody = "Hello from the email POC."
};

static void Title(string text)
{
    Thread.Sleep(150); // demo only: the console logger writes in the background; let it catch up so sections don't mix
    Console.WriteLine($"\n=== {text} ===");
}

static void Print(EmailSendResult r) => Console.WriteLine(r.Success
    ? $"RESULT: OK via {r.Provider} (id: {r.MessageId})"
    : $"RESULT: FAILED via {r.Provider}: {r.Error}");

public partial class Program;
