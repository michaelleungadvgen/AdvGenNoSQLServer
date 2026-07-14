// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Network;
using System.Text.Json;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

/// <summary>
/// NoSqlMessage.CreateCommand must produce valid JSON for any command/collection
/// value (audit D3: the previous string concatenation broke on quotes/backslashes).
/// </summary>
public class MessageProtocolCreateCommandTests
{
    [Fact]
    public void CreateCommand_EscapesSpecialCharactersInCollection()
    {
        var message = NoSqlMessage.CreateCommand("get", "we\"ird\\name");
        using var doc = JsonDocument.Parse(message.GetPayloadAsString()); // must be valid JSON
        Assert.Equal("get", doc.RootElement.GetProperty("command").GetString());
        Assert.Equal("we\"ird\\name", doc.RootElement.GetProperty("collection").GetString());
    }

    [Fact]
    public void CreateCommand_WithDocument_KeepsWireShape()
    {
        var message = NoSqlMessage.CreateCommand("set", "col", new { name = "x", n = 1 });
        using var doc = JsonDocument.Parse(message.GetPayloadAsString());
        Assert.Equal("set", doc.RootElement.GetProperty("command").GetString());
        Assert.Equal("col", doc.RootElement.GetProperty("collection").GetString());
        Assert.Equal("x", doc.RootElement.GetProperty("document").GetProperty("name").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("document").GetProperty("n").GetInt32());
    }

    [Fact]
    public void CreateCommand_WithoutDocument_OmitsDocumentProperty()
    {
        var message = NoSqlMessage.CreateCommand("count", "col");
        using var doc = JsonDocument.Parse(message.GetPayloadAsString());
        Assert.False(doc.RootElement.TryGetProperty("document", out _));
    }

    [Fact]
    public void CreateCommand_UnicodeCollectionName_RoundTrips()
    {
        var message = NoSqlMessage.CreateCommand("count", "коллекция-日本語");
        using var doc = JsonDocument.Parse(message.GetPayloadAsString());
        Assert.Equal("коллекция-日本語", doc.RootElement.GetProperty("collection").GetString());
    }
}
