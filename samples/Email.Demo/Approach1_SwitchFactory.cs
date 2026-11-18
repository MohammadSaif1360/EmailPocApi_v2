using Company.Email;

namespace Email.Demo;

/// <summary>
/// Approach 1 (Strategy + Factory) — kept ONLY for comparison in the POC.
/// It works, but every new provider means editing this switch (violates Open/Closed).
/// The DI approach in Program.cs needs no such central list.
/// X++ analogy: a construct() method switching on an enum.
/// </summary>
public static class Approach1_SwitchFactory
{
    public static IEmailProvider Create(string name) => name switch
    {
        "Console" => new ConsoleEmailProvider("Console"),
        // "SendGrid" => new SendGridEmailProvider(...),
        // "AwsSes"   => new AwsSesEmailProvider(...),
        // Adding Mailgun? You must come back and edit this file.
        _ => throw new ArgumentException($"Unknown provider '{name}'")
    };
}
