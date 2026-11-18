using System.Reflection;
using Company.Email;
using Company.Email.AzureAcs;
using Company.Email.Smtp;

var builder = WebApplication.CreateBuilder(args);

// ── 1. Email library: the ONLY place this app decides how email is sent ──────────
//    Which provider is used comes from appsettings.json ("Email:DefaultProvider").
//    Secrets (ACS connection string, SMTP password) come from user-secrets / Key Vault.
var emailSection = builder.Configuration.GetSection("Email");

builder.Services.AddEmailNotifications(emailSection)
    .AddAzureAcs(emailSection.GetSection("AzureAcs"))   // real sending via Azure Communication Services
    .AddSmtp(emailSection.GetSection("Smtp"))           // optional backup
    .AddConsoleProvider();                              // prints to the console; handy before ACS is set up

// ── 2. Standard Web API setup ────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new() { Title = "Email Notification API", Version = "v1",
        Description = "POC: one API, any email provider. Provider is chosen in appsettings.json." });

    // Show the /// comments from the controller and models in Swagger.
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) o.IncludeXmlComments(xml);
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();
