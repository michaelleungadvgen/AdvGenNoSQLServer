// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Authentication;
using Xunit;

namespace AdvGenNoSqlServer.Tests;

public class CommandAuthorizerTests
{
    [Theory]
    [InlineData("get", UserRole.ReadOnly, true)]
    [InlineData("count", UserRole.ReadOnly, true)]
    [InlineData("set", UserRole.ReadOnly, false)]
    [InlineData("delete", UserRole.ReadOnly, false)]
    [InlineData("createuser", UserRole.ReadOnly, false)]
    [InlineData("set", UserRole.ReadWrite, true)]
    [InlineData("createcollection", UserRole.ReadWrite, true)]
    [InlineData("listusers", UserRole.ReadWrite, false)]
    [InlineData("setrole", UserRole.ReadWrite, false)]
    [InlineData("get", UserRole.Admin, true)]
    [InlineData("set", UserRole.Admin, true)]
    [InlineData("createuser", UserRole.Admin, true)]
    [InlineData("deleteuser", UserRole.Admin, true)]
    [InlineData("listattachments", UserRole.ReadOnly, true)]
    [InlineData("downloadattachment", UserRole.ReadOnly, true)]
    [InlineData("attachmentinfo", UserRole.ReadOnly, true)]
    [InlineData("totalstorage", UserRole.ReadOnly, true)]
    [InlineData("uploadattachment", UserRole.ReadOnly, false)]
    [InlineData("deleteattachment", UserRole.ReadOnly, false)]
    [InlineData("uploadattachment", UserRole.ReadWrite, true)]
    [InlineData("deleteattachment", UserRole.ReadWrite, true)]
    [InlineData("uploadattachment", UserRole.Admin, true)]
    public void IsAllowed_MatrixMatchesSpec(string command, string role, bool expected)
        => Assert.Equal(expected, CommandAuthorizer.IsAllowed(command, role));

    [Fact]
    public void IsAllowed_ChangePassword_AllowedForAnyRole()
    {
        Assert.True(CommandAuthorizer.IsAllowed("changepassword", UserRole.ReadOnly));
        Assert.True(CommandAuthorizer.IsAllowed("changepassword", UserRole.Admin));
    }

    [Fact]
    public void IsAllowed_UnknownCommand_PassesThrough()
        => Assert.True(CommandAuthorizer.IsAllowed("totally-unknown", UserRole.ReadOnly));

    [Fact]
    public void IsAllowed_IsCaseInsensitiveOnCommand()
        => Assert.False(CommandAuthorizer.IsAllowed("SET", UserRole.ReadOnly));
}
