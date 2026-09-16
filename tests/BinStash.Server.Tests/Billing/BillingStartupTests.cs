// Copyright (C) Lukas Eßmann — AGPLv3 or later

using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BinStash.Infrastructure.Data;

namespace BinStash.Server.Tests.Billing;

public class BillingStartupTests
{
    /// <summary>
    /// Boots the real server the way these smoke tests need it: with no logging providers.
    /// </summary>
    /// <remarks>
    /// <c>Program.cs</c> calls <c>AddWindowsService()</c>, which attaches the Windows Event Log
    /// provider whenever the process looks like a service — and on a Jenkins agent that itself
    /// runs as a Windows service, that is true of the test process too. Writing to the
    /// '.NET Runtime' source then needs permissions the build account does not have, the logger
    /// throws, and because the first thing to log is an options validator it throws out of
    /// <c>Host.StartAsync</c> and fails startup outright. The host lifetime is already
    /// neutralised by <see cref="WebApplicationFactory{T}"/>; logging is not. Clearing the
    /// providers fixes it on every agent, and a test host has no business writing to the
    /// machine's event log regardless.
    /// </remarks>
    private static WebApplicationFactory<BinStashServerEntryPoint> CreateFactory()
        => new WebApplicationFactory<BinStashServerEntryPoint>()
            .WithWebHostBuilder(host => host.ConfigureLogging(logging => logging.ClearProviders()));

    [Fact]
    public void NoPluginPath_ServerBuildsSuccessfully()
    {
        // Arrange: no BINSTASH_BILLING_PLUGIN_PATH set — no-op billing path
        // Override DB so the server can start without a real PostgreSQL instance
        var factory = CreateFactory()
            .WithWebHostBuilder(host =>
            {
                host.ConfigureServices(services =>
                {
                    // Remove the Npgsql DbContext registration and replace with InMemory
                    // to avoid needing a real PostgreSQL instance for this smoke test
                    var toRemove = services
                        .Where(d => d.ServiceType == typeof(DbContextOptions<BinStashDbContext>)
                                 || (d.ServiceType.IsGenericType
                                     && d.ServiceType.GetGenericTypeDefinition() == typeof(IDbContextOptionsConfiguration<>)
                                     && d.ServiceType.GenericTypeArguments[0] == typeof(BinStashDbContext)))
                        .ToList();
                    foreach (var d in toRemove) services.Remove(d);

                    services.AddDbContext<BinStashDbContext>(options =>
                        options.UseInMemoryDatabase("BillingStartupTest_NoPlugin"));
                });
            });

        // Act & Assert: creating a client should not throw
        var act = () => factory.CreateClient();
        act.Should().NotThrow();

        factory.Dispose();
    }

    [Fact]
    public void InvalidPluginPath_ThrowsOnBuild()
    {
        // Arrange: set BINSTASH_BILLING_PLUGIN_PATH to a nonexistent DLL via environment variable
        Environment.SetEnvironmentVariable("BINSTASH_BILLING_PLUGIN_PATH", "/nonexistent/path/billing.dll");
        try
        {
            var factory = CreateFactory();

            // Act & Assert: startup should throw InvalidOperationException from BillingPluginLoader
            var act = () => factory.CreateClient();
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Failed to load billing plugin*");

            factory.Dispose();
        }
        finally
        {
            Environment.SetEnvironmentVariable("BINSTASH_BILLING_PLUGIN_PATH", null);
        }
    }
}
