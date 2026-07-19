// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Configuration;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Tests for ServerConfiguration.Validate and environment-overlay config loading.
/// </summary>
public class ConfigurationValidationTests
{
    [Fact]
    public void Validate_DevelopmentDefaults_ReturnsNoErrors()
    {
        var config = new ServerConfiguration();
        Assert.Empty(config.Validate(isProduction: false));
    }

    [Fact]
    public void Validate_ProductionWithoutSecrets_ReturnsErrors()
    {
        var config = new ServerConfiguration(); // no MasterPassword / JwtSecretKey
        var errors = config.Validate(isProduction: true);

        Assert.Contains(errors, e => e.Contains("MasterPassword"));
        Assert.Contains(errors, e => e.Contains("JwtSecretKey"));
    }

    [Fact]
    public void Validate_ProductionWithSecrets_ReturnsNoErrors()
    {
        var config = new ServerConfiguration
        {
            MasterPassword = "a-very-strong-master-password",
            JwtSecretKey = "a-very-strong-jwt-secret-with-32-plus-chars"
        };
        Assert.Empty(config.Validate(isProduction: true));
    }

    [Fact]
    public void Validate_ProductionRejectsDefaultMasterPassword()
    {
        var config = new ServerConfiguration
        {
            MasterPassword = "admin123",
            JwtSecretKey = "a-very-strong-jwt-secret-with-32-plus-chars"
        };
        Assert.Contains(config.Validate(isProduction: true), e => e.Contains("admin123"));
    }

    [Fact]
    public void Validate_ProductionRejectsDevJwtSecret()
    {
        var config = new ServerConfiguration
        {
            MasterPassword = "a-very-strong-master-password",
            JwtSecretKey = "AdvGenNoSQL-DefaultDevSecret-ChangeInProduction-2026!"
        };
        Assert.Contains(config.Validate(isProduction: true), e => e.Contains("development JWT secret"));
    }

    [Fact]
    public void Validate_ProductionRequiresAuthentication()
    {
        var config = new ServerConfiguration
        {
            RequireAuthentication = false,
            MasterPassword = "a-very-strong-master-password",
            JwtSecretKey = "a-very-strong-jwt-secret-with-32-plus-chars"
        };
        Assert.Contains(config.Validate(isProduction: true), e => e.Contains("RequireAuthentication"));
    }

    [Fact]
    public void Validate_InvalidPort_ReturnsError()
    {
        var config = new ServerConfiguration { Port = 0 };
        Assert.Contains(config.Validate(isProduction: false), e => e.Contains("Port"));
    }

    [Fact]
    public void Validate_SslEnabledButCertMissing_ReturnsError()
    {
        var config = new ServerConfiguration
        {
            EnableSsl = true,
            SslCertificatePath = "/nonexistent/cert.pfx"
        };
        Assert.Contains(config.Validate(isProduction: false), e => e.Contains("SslCertificatePath"));
    }

    [Fact]
    public void Validate_EmptyAnonymousRole_ReturnsError()
    {
        var config = new ServerConfiguration { AnonymousRole = "" };
        Assert.Contains(config.Validate(isProduction: false), e => e.Contains("AnonymousRole"));
    }

    [Fact]
    public void ConfigurationManager_EnvironmentOverlay_OverridesBaseFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "advgen-config-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var previousEnv = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        try
        {
            File.WriteAllText(Path.Combine(dir, "appsettings.json"),
                """{ "Port": 1000, "StoragePath": "base-path" }""");
            File.WriteAllText(Path.Combine(dir, "appsettings.TestOverlay.json"),
                """{ "Port": 2000 }""");

            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "TestOverlay");
            var mgr = new ConfigurationManager(Path.Combine(dir, "appsettings.json"));

            Assert.Equal(2000, mgr.Configuration.Port);            // overlay wins
            Assert.Equal("base-path", mgr.Configuration.StoragePath); // base survives
            Assert.False(mgr.IsProduction);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", previousEnv);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void ConfigurationManager_MalformedJson_ThrowsInProduction()
    {
        var dir = Path.Combine(Path.GetTempPath(), "advgen-config-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var previousEnv = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        try
        {
            File.WriteAllText(Path.Combine(dir, "appsettings.json"), "{ not valid json !!!");
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Production");

            Assert.Throws<InvalidOperationException>(
                () => new ConfigurationManager(Path.Combine(dir, "appsettings.json")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", previousEnv);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
