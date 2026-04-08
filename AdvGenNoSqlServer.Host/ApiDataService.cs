// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
using AdvGenNoSqlServer.Core.Abstractions;
using AdvGenNoSqlServer.Network;

namespace AdvGenNoSqlServer.Host;

/// <summary>
/// Holds live references to the document store and TCP server
/// so HTTP API endpoints can query real data.
/// </summary>
public class ApiDataService
{
    public IDocumentStore? DocumentStore { get; set; }
    public TcpServer? TcpServer { get; set; }
    public DateTime StartTime { get; } = DateTime.UtcNow;
}
