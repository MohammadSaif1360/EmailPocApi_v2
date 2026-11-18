using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Company.Email;

/// <summary>Returned by AddEmailNotifications so provider packages can chain .AddSendGrid(), .AddAwsSes()...</summary>
public sealed class EmailBuilder
{
    internal EmailBuilder(IServiceCollection services, EmailProviderRegistry registry)
    {
        Services = services;
        Registry = registry;
    }

    public IServiceCollection Services { get; }
    internal EmailProviderRegistry Registry { get; }

    /// <summary>
    /// Registers a provider under a name. Provider packages call this from their own
    /// extension method; apps can also call it directly for a custom provider.
    /// </summary>
    public EmailBuilder AddProvider(string name, Func<IServiceProvider, IEmailProvider> factory)
    {
        Registry.Add(name);
        Services.AddKeyedSingleton(name, (sp, _) => factory(sp));
        return this;
    }
}

public static class ServiceCollectionExtensions
{
    /// <summary>Registers IEmailService and binds EmailOptions from configuration (usually the "Email" section).</summary>
    public static EmailBuilder AddEmailNotifications(this IServiceCollection services, IConfiguration section)
    {
        services.Configure<EmailOptions>(section);
        return AddCore(services);
    }

    /// <summary>Registers IEmailService with options set in code.</summary>
    public static EmailBuilder AddEmailNotifications(this IServiceCollection services, Action<EmailOptions> configure)
    {
        services.Configure(configure);
        return AddCore(services);
    }

    /// <summary>Dev/test provider that prints emails to the console instead of sending them.</summary>
    public static EmailBuilder AddConsoleProvider(this EmailBuilder builder, string name = "Console") =>
        builder.AddProvider(name, _ => new ConsoleEmailProvider(name));

    private static EmailBuilder AddCore(IServiceCollection services)
    {
        var registry = new EmailProviderRegistry();
        services.AddSingleton(registry);
        services.AddLogging();
        services.AddSingleton<IEmailService, EmailService>();
        return new EmailBuilder(services, registry);
    }
}
