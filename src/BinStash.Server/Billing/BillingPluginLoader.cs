// Copyright (C) Lukas Eßmann — AGPLv3 or later

using BinStash.Core.Billing;
using System.Reflection;

namespace BinStash.Server.Billing;

public sealed class BillingPluginLoader
{
    private IBillingPluginRegistrar? _registrar;

    // SECURITY: BillingPluginPath must come from environment variable only, never from DbConfigurationSource
    private const string PluginPathConfigKey = "BINSTASH_BILLING_PLUGIN_PATH";

    public void LoadAndRegisterServices(WebApplicationBuilder builder)
    {
        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<BillingPluginLoader>();

        var path = builder.Configuration[PluginPathConfigKey];

        if (string.IsNullOrWhiteSpace(path))
        {
            logger.LogInformation("No billing plugin configured, using no-op provider");
            return;
        }

        Assembly assembly;
        try
        {
            assembly = Assembly.LoadFrom(path);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load billing plugin from {path}: {ex.Message}", ex);
        }

        var registrarType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IBillingPluginRegistrar).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

        if (registrarType is null)
        {
            throw new InvalidOperationException($"Billing plugin at {path} does not implement IBillingPluginRegistrar");
        }

        var registrar = (IBillingPluginRegistrar)Activator.CreateInstance(registrarType)!;
        RegisterFromRegistrar(builder.Services, builder.Configuration, registrar);
    }

    /// <summary>
    /// Internal entry point for testing: directly supply a registrar instance.
    /// </summary>
    internal void RegisterFromRegistrar(
        IServiceCollection services,
        IConfiguration configuration,
        IBillingPluginRegistrar registrar)
    {
        _registrar = registrar;
        _registrar.Register(services, configuration);
    }

    public void MapPluginEndpoints(WebApplication app)
    {
        _registrar?.MapEndpoints(app);
    }
}
