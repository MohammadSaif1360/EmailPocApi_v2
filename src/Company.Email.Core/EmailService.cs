using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Company.Email;

/// <summary>Keeps the names of all registered providers, for lookups and clear error messages.</summary>
public sealed class EmailProviderRegistry
{
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Names => _names;

    internal void Add(string name)
    {
        if (!_names.Add(name))
            throw new InvalidOperationException($"An email provider named '{name}' is already registered.");
    }

    public bool Contains(string name) => _names.Contains(name);
}

/// <summary>
/// The "front desk". Validates the message, looks the provider up by name in the DI container
/// (no if/switch), and wraps it with logging, retry and fallback.
/// </summary>
internal sealed class EmailService(
    IServiceProvider services,
    EmailProviderRegistry registry,
    IOptions<EmailOptions> options,
    ILoggerFactory loggerFactory) : IEmailService
{
    private readonly ILogger _logger = loggerFactory.CreateLogger("Company.Email");

    public async Task<EmailSendResult> SendAsync(EmailMessage message, string? provider = null, CancellationToken ct = default)
    {
        var opts = options.Value;
        var primaryName = provider ?? opts.DefaultProvider;

        if (string.IsNullOrWhiteSpace(primaryName))
            throw new InvalidOperationException("No provider given and Email:DefaultProvider is not configured.");

        var errors = EmailMessageValidator.Validate(message, opts);
        if (errors.Count > 0)
            return EmailSendResult.Fail(primaryName, string.Join(" ", errors));

        // An explicit provider choice is respected as-is; the default gets the fallback chain.
        var chainNames = provider is null
            ? new[] { primaryName }.Concat(opts.FallbackProviders).Distinct(StringComparer.OrdinalIgnoreCase)
            : [primaryName];

        var chain = chainNames
            .Select(name => (IEmailProvider)new LoggingEmailProvider(
                new RetryEmailProvider(Resolve(name), opts.Retry, _logger), _logger))
            .ToList();

        var pipeline = chain.Count == 1 ? chain[0] : new FallbackEmailProvider(chain, _logger);
        return await pipeline.SendAsync(message, ct);
    }

    private IEmailProvider Resolve(string name)
    {
        if (!registry.Contains(name))
            throw new InvalidOperationException(
                $"Email provider '{name}' is not registered. Registered: {string.Join(", ", registry.Names)}.");

        return services.GetRequiredKeyedService<IEmailProvider>(name);
    }
}
