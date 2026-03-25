// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Core.Configuration;

/// <summary>
/// Configuration for Application-Layer Protocol Negotiation (ALPN)
/// ALPN allows the client and server to negotiate which application protocol to use 
/// during the TLS handshake, improving connection establishment performance.
/// </summary>
public class AlpnConfiguration
{
    /// <summary>
    /// Default protocol versions supported by the server
    /// </summary>
    public static readonly string[] DefaultProtocols = { "nosql/1.1", "nosql/1.0" };

    /// <summary>
    /// Whether ALPN is enabled (default: false)
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The list of supported application protocols in order of preference
    /// Default: ["nosql/1.1", "nosql/1.0"]
    /// </summary>
    public List<string> Protocols { get; set; } = new(DefaultProtocols);

    /// <summary>
    /// Whether ALPN negotiation is required (default: false)
    /// When true, connections that don't negotiate a protocol will be rejected
    /// When false, connections without ALPN support will be allowed
    /// </summary>
    public bool RequireAlpn { get; set; } = false;

    /// <summary>
    /// The default protocol to use when ALPN negotiation fails and RequireAlpn is false
    /// If null, the first protocol in the list is used as default
    /// </summary>
    public string? DefaultProtocol { get; set; } = "nosql/1.0";

    /// <summary>
    /// Validates the ALPN configuration
    /// </summary>
    /// <returns>True if valid, false otherwise</returns>
    public bool Validate()
    {
        if (!Enabled)
            return true;

        // Must have at least one protocol if enabled
        if (Protocols == null || Protocols.Count == 0)
            return false;

        // All protocols must be non-empty
        if (Protocols.Any(string.IsNullOrWhiteSpace))
            return false;

        // Default protocol must be in the supported list if specified
        if (!string.IsNullOrWhiteSpace(DefaultProtocol) && !Protocols.Contains(DefaultProtocol))
            return false;

        return true;
    }

    /// <summary>
    /// Gets the default protocol to use when negotiation fails
    /// </summary>
    public string GetDefaultProtocol()
    {
        if (!string.IsNullOrWhiteSpace(DefaultProtocol))
            return DefaultProtocol;

        return Protocols.FirstOrDefault() ?? "nosql/1.0";
    }

    /// <summary>
    /// Creates a clone of this configuration
    /// </summary>
    public AlpnConfiguration Clone()
    {
        return new AlpnConfiguration
        {
            Enabled = Enabled,
            Protocols = new List<string>(Protocols),
            RequireAlpn = RequireAlpn,
            DefaultProtocol = DefaultProtocol
        };
    }
}

/// <summary>
/// Result of ALPN protocol negotiation
/// </summary>
public class AlpnNegotiationResult
{
    /// <summary>
    /// Whether ALPN negotiation was successful
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// The negotiated protocol (null if negotiation failed or ALPN not used)
    /// </summary>
    public string? NegotiatedProtocol { get; }

    /// <summary>
    /// The protocol selected by the server from its supported list
    /// </summary>
    public string? ServerSelectedProtocol { get; }

    /// <summary>
    /// The protocol requested by the client (if any)
    /// </summary>
    public string? ClientRequestedProtocol { get; }

    /// <summary>
    /// Whether ALPN was offered by the client
    /// </summary>
    public bool AlpnOfferedByClient { get; }

    /// <summary>
    /// Error message if negotiation failed
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Creates a successful negotiation result
    /// </summary>
    public AlpnNegotiationResult(string negotiatedProtocol, string? clientRequested = null)
    {
        Success = true;
        NegotiatedProtocol = negotiatedProtocol;
        ServerSelectedProtocol = negotiatedProtocol;
        ClientRequestedProtocol = clientRequested;
        AlpnOfferedByClient = clientRequested != null;
    }

    /// <summary>
    /// Creates a failed negotiation result
    /// </summary>
    public AlpnNegotiationResult(string errorMessage, bool alpnOffered = true)
    {
        Success = false;
        ErrorMessage = errorMessage;
        AlpnOfferedByClient = alpnOffered;
    }

    /// <summary>
    /// Private constructor for factory methods
    /// </summary>
    private AlpnNegotiationResult(string? negotiatedProtocol, string? clientRequested, bool alpnOffered, bool success)
    {
        Success = success;
        NegotiatedProtocol = negotiatedProtocol;
        ServerSelectedProtocol = negotiatedProtocol;
        ClientRequestedProtocol = clientRequested;
        AlpnOfferedByClient = alpnOffered;
    }

    /// <summary>
    /// Creates a result for when ALPN was not offered by client
    /// </summary>
    public static AlpnNegotiationResult NoAlpnOffered(string? defaultProtocol = null)
    {
        return new AlpnNegotiationResult(defaultProtocol, null, false, defaultProtocol != null);
    }

    /// <summary>
    /// Factory method for success
    /// </summary>
    public static AlpnNegotiationResult CreateSuccess(string protocol, string? clientRequested = null)
    {
        return new AlpnNegotiationResult(protocol, clientRequested);
    }

    /// <summary>
    /// Factory method for failure
    /// </summary>
    public static AlpnNegotiationResult CreateFailure(string errorMessage)
    {
        return new AlpnNegotiationResult(errorMessage, alpnOffered: true);
    }
}

/// <summary>
/// Exception thrown when ALPN negotiation fails
/// </summary>
public class AlpnException : Exception
{
    /// <summary>
    /// The server protocols that were offered
    /// </summary>
    public IReadOnlyList<string> ServerProtocols { get; }

    /// <summary>
    /// The client protocols that were requested (if known)
    /// </summary>
    public IReadOnlyList<string>? ClientProtocols { get; }

    /// <summary>
    /// Whether this was a required ALPN failure
    /// </summary>
    public bool WasRequired { get; }

    /// <summary>
    /// Creates a new AlpnException
    /// </summary>
    public AlpnException(
        string message,
        IEnumerable<string> serverProtocols,
        bool wasRequired = false,
        IEnumerable<string>? clientProtocols = null)
        : base(message)
    {
        ServerProtocols = serverProtocols.ToList().AsReadOnly();
        WasRequired = wasRequired;
        ClientProtocols = clientProtocols?.ToList().AsReadOnly();
    }

    /// <summary>
    /// Creates a new AlpnException with inner exception
    /// </summary>
    public AlpnException(
        string message,
        Exception innerException,
        IEnumerable<string> serverProtocols,
        bool wasRequired = false)
        : base(message, innerException)
    {
        ServerProtocols = serverProtocols.ToList().AsReadOnly();
        WasRequired = wasRequired;
    }

    /// <summary>
    /// Creates an exception for when no common protocol is found
    /// </summary>
    public static AlpnException NoCommonProtocol(
        IEnumerable<string> serverProtocols,
        IEnumerable<string> clientProtocols)
    {
        var serverList = string.Join(", ", serverProtocols);
        var clientList = string.Join(", ", clientProtocols);

        return new AlpnException(
            $"ALPN negotiation failed: No common protocol found. " +
            $"Server supports: [{serverList}], " +
            $"Client offered: [{clientList}]",
            serverProtocols,
            wasRequired: true,
            clientProtocols);
    }

    /// <summary>
    /// Creates an exception for when ALPN is required but not provided
    /// </summary>
    public static AlpnException AlpnRequired(IEnumerable<string> serverProtocols)
    {
        var serverList = string.Join(", ", serverProtocols);

        return new AlpnException(
            $"ALPN is required but client did not offer any protocols. " +
            $"Server requires one of: [{serverList}]",
            serverProtocols,
            wasRequired: true);
    }

    /// <summary>
    /// Creates an exception for an unsupported protocol
    /// </summary>
    public static AlpnException UnsupportedProtocol(
        string protocol,
        IEnumerable<string> serverProtocols)
    {
        var serverList = string.Join(", ", serverProtocols);

        return new AlpnException(
            $"ALPN negotiation failed: Protocol '{protocol}' is not supported. " +
            $"Server supports: [{serverList}]",
            serverProtocols,
            wasRequired: false);
    }
}
