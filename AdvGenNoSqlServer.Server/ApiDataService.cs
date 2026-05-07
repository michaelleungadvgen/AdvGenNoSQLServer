// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Network;

namespace AdvGenNoSqlServer.Server;

/// <summary>
/// Holds live references to the document store, database manager, and TCP server
/// so HTTP API endpoints can query real data.
/// </summary>
public class ApiDataService
{
    public IDatabaseManager? DatabaseManager { get; set; }
    public IDocumentStore? DocumentStore { get; set; }
    public TcpServer? TcpServer { get; set; }
    public DateTime StartTime { get; } = DateTime.UtcNow;
}
