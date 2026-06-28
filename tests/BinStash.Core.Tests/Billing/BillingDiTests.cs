// Copyright (C) Lukas Eßmann — AGPLv3 or later

using BinStash.Core.Billing;
using BinStash.Core.Billing.NoOp;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace BinStash.Core.Tests.Billing;

public class BillingDiTests
{
    [Fact]
    public void AddNoOpBilling_resolves_noop_services()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        InvokeAddNoOpBilling(services);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBillingProvider>().Should().BeOfType<NoOpBillingProvider>();
        provider.GetRequiredService<IUsageMeteringService>().Should().BeOfType<NoOpUsageMeteringService>();
    }

    private static void InvokeAddNoOpBilling(IServiceCollection services)
    {
        var assemblyPath = ResolveServerAssemblyPath();
        var assembly = Assembly.LoadFrom(assemblyPath);
        var type = assembly.GetType("BinStash.Server.BillingServiceCollectionExtensions", throwOnError: true)!;
        var method = type.GetMethod("AddNoOpBilling", BindingFlags.Public | BindingFlags.Static, [typeof(IServiceCollection)])!;

        method.Invoke(null, [services]);
    }

    private static string ResolveServerAssemblyPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "BinStash.Server", "bin", "Release", "net10.0", "BinStash.Server.dll")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "BinStash.Server", "bin", "Debug", "net10.0", "BinStash.Server.dll"))
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Could not locate BinStash.Server.dll", string.Join(Environment.NewLine, candidates));
    }
}
