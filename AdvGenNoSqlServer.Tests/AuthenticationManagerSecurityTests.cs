// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Authentication;
using AdvGenNoSqlServer.Core.Configuration;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// Tests for the AuthenticationManager hardening features: login lockout and
/// per-hash PBKDF2 iteration counts. Uses the minimum allowed iteration count
/// (10k) to keep the suite fast.
/// </summary>
public class AuthenticationManagerSecurityTests
{
    private static AuthenticationManager CreateManager(int maxFailedAttempts = 3, int iterations = 10_000)
        => new(new ServerConfiguration
        {
            Pbkdf2Iterations = iterations,
            MaxFailedLoginAttempts = maxFailedAttempts,
            LockoutMinutes = 15
        });

    [Fact]
    public void Authenticate_LockoutAfterMaxFailures_BlocksCorrectPassword()
    {
        var mgr = CreateManager(maxFailedAttempts: 3);
        mgr.RegisterUser("alice", "hunter2");

        Assert.Null(mgr.Authenticate("alice", "wrong1"));
        Assert.Null(mgr.Authenticate("alice", "wrong2"));
        Assert.Null(mgr.Authenticate("alice", "wrong3")); // triggers lockout

        Assert.Null(mgr.Authenticate("alice", "hunter2")); // locked: even correct password fails
    }

    [Fact]
    public void Authenticate_SuccessfulLogin_ResetsFailureCounter()
    {
        var mgr = CreateManager(maxFailedAttempts: 3);
        mgr.RegisterUser("bob", "hunter2");

        Assert.Null(mgr.Authenticate("bob", "wrong1"));
        Assert.Null(mgr.Authenticate("bob", "wrong2"));
        Assert.NotNull(mgr.Authenticate("bob", "hunter2")); // resets the counter
        Assert.Null(mgr.Authenticate("bob", "wrong3"));
        Assert.Null(mgr.Authenticate("bob", "wrong4"));
        Assert.NotNull(mgr.Authenticate("bob", "hunter2")); // still not locked
    }

    [Fact]
    public void Authenticate_LockoutDisabled_WhenMaxAttemptsZero()
    {
        var mgr = CreateManager(maxFailedAttempts: 0);
        mgr.RegisterUser("carol", "hunter2");

        for (int i = 0; i < 10; i++)
            Assert.Null(mgr.Authenticate("carol", $"wrong{i}"));

        Assert.NotNull(mgr.Authenticate("carol", "hunter2"));
    }

    [Fact]
    public void Authenticate_UnknownUser_ReturnsNull()
    {
        var mgr = CreateManager();
        Assert.Null(mgr.Authenticate("nosuchuser", "whatever"));
    }

    [Fact]
    public void RegisterUser_RecordsConfiguredIterations()
    {
        var mgr = CreateManager(iterations: 10_000);
        mgr.RegisterUser("dave", "hunter2");

        Assert.Equal(10_000, mgr.GetUsers()["dave"].Iterations);
    }

    [Fact]
    public void Authenticate_LegacyHashWithoutIterations_Verifies()
    {
        // Hashes written before iterations were recorded (Iterations = 0) were made
        // at the legacy 100k count and must keep verifying.
        var mgr = CreateManager(iterations: 100_000);
        mgr.RegisterUser("erin", "hunter2");
        mgr.GetUsers()["erin"].Iterations = 0; // simulate a pre-hardening record

        Assert.NotNull(mgr.Authenticate("erin", "hunter2"));
    }

    [Fact]
    public void FileUserStore_LegacyFileWithoutIterations_StillAuthenticates()
    {
        var dir = Path.Combine(Path.GetTempPath(), "advgen-auth-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var storePath = Path.Combine(dir, "users.json");

            // Write a user at the legacy iteration count.
            var writer = new AuthenticationManager(
                new ServerConfiguration { Pbkdf2Iterations = 100_000 },
                new FileUserStore(storePath));
            writer.RegisterUser("frank", "hunter2");

            // Strip the Iterations field to simulate a pre-hardening users.json.
            var json = File.ReadAllText(storePath);
            Assert.DoesNotContain("\"Iterations\": 0", json); // sanity: field is omitted when 0? if present, remove it
            json = System.Text.RegularExpressions.Regex.Replace(json, ",\\s*\"Iterations\":\\s*\\d+", "");
            File.WriteAllText(storePath, json);

            var reader = new AuthenticationManager(
                new ServerConfiguration { Pbkdf2Iterations = 10_000 },
                new FileUserStore(storePath));

            Assert.NotNull(reader.Authenticate("frank", "hunter2"));
            Assert.Equal(0, reader.GetUsers()["frank"].Iterations); // loaded as legacy
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
