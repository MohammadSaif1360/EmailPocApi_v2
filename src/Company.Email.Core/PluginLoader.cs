using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Configuration;

namespace Company.Email;

/// <summary>
/// Approach 3 (runtime plugins). A plugin DLL contains a class implementing this interface.
/// The host finds it at startup and lets it register its provider — no project reference, no recompile.
/// </summary>
public interface IEmailProviderPlugin
{
    /// <summary>Register the plugin's provider(s) on the builder, reading settings from the given "Email" section.</summary>
    void Register(EmailBuilder builder, IConfiguration emailSection);
}

public static class PluginLoader
{
    /// <summary>
    /// Loads every *.dll in <paramref name="folder"/> and runs each IEmailProviderPlugin found.
    /// Plugins share the host's Company.Email assemblies, so build them against the same version.
    /// Only load DLLs you trust: plugin code runs with the app's full permissions.
    /// </summary>
    public static EmailBuilder AddPluginsFromFolder(this EmailBuilder builder, string folder, IConfiguration emailSection)
    {
        if (!Directory.Exists(folder))
            return builder;

        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .GroupBy(a => a.GetName().Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var dll in Directory.GetFiles(folder, "*.dll"))
        {
            Assembly assembly;
            try
            {
                // Reuse an assembly that is already loaded (e.g. the host's own Company.Email.Core.dll,
                // or a plugin loaded by an earlier call) instead of loading a second copy.
                var assemblyName = AssemblyName.GetAssemblyName(dll).Name!;
                assembly = loaded.TryGetValue(assemblyName, out var existing)
                    ? existing
                    : AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(dll));
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException)
            {
                continue; // not a .NET assembly, or a conflicting version
            }

            var pluginTypes = assembly.GetTypes().Where(t =>
                typeof(IEmailProviderPlugin).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });

            foreach (var type in pluginTypes)
            {
                var plugin = (IEmailProviderPlugin)Activator.CreateInstance(type)!;
                plugin.Register(builder, emailSection);
            }
        }

        return builder;
    }
}
