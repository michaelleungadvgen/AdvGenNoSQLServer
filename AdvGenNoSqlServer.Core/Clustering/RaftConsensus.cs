// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdvGenNoSqlServer.Core.Clustering
{
    /// <summary>
    /// Defines the role of a node in Raft consensus.
    /// </summary>
    public enum RaftRole
    {
        /// <summary>
        /// Follower - receives log entries from leader.
        /// </summary>
        Follower,

        /// <summary>
        /// Candidate - requesting votes to become leader.
        /// </summary>
        Candidate,

        /// <summary>
        /// Leader - handles all client requests and replicates log.
        /// </summary>
        Leader
    }

    /// <summary>
    /// Interface for Raft consensus protocol implementation.
    /// </summary>
    public interface IRaftConsensus : IDisposable
    {
        /// <summary>
        /// Gets the current role of this node.
        /// </summary>
        RaftRole CurrentRole { get; }

        /// <summary>
        /// Gets the ID of the current leader (null if unknown).
        /// </summary>
        string? CurrentLeaderId { get; }

        /// <summary>
        /// Gets the current term number.
        /// </summary>
        long CurrentTerm { get; }

        /// <summary>
        /// Gets the local node ID.
        /// </summary>
        string LocalNodeId { get; }

        /// <summary>
        /// Gets whether this node is the current leader.
        /// </summary>
        bool IsLeader => CurrentRole == RaftRole.Leader;

        /// <summary>
        /// Starts the Raft consensus engine.
        /// </summary>
        Task StartAsync(CancellationToken ct = default);

        /// <summary>
        /// Stops the Raft consensus engine.
        /// </summary>
        Task StopAsync(CancellationToken ct = default);

        /// <summary>
        /// Proposes a new log entry to be replicated.
        /// Only the leader can propose entries.
        /// </summary>
        Task<ProposeResult> ProposeAsync(RaftLogEntry entry, CancellationToken ct = default);

        /// <summary>
        /// Handles a vote request from another node.
        /// </summary>
        Task<VoteResponse> HandleVoteRequestAsync(VoteRequest request, CancellationToken ct = default);

        /// <summary>
        /// Handles an append entries request from the leader.
        /// </summary>
        Task<AppendResponse> HandleAppendEntriesAsync(AppendRequest request, CancellationToken ct = default);

        /// <summary>
        /// Gets the current log entries.
        /// </summary>
        IReadOnlyList<RaftLogEntry> GetLogEntries();

        /// <summary>
        /// Gets statistics about the Raft state.
        /// </summary>
        RaftStatistics GetStatistics();

        /// <summary>
        /// Event raised when the role changes.
        /// </summary>
        event EventHandler<RoleChangedEventArgs>? RoleChanged;

        /// <summary>
        /// Event raised when the leader changes.
        /// </summary>
        event EventHandler<LeaderChangedEventArgs>? LeaderChanged;

        /// <summary>
        /// Event raised when a log entry is committed.
        /// </summary>
        event EventHandler<LogCommittedEventArgs>? LogCommitted;
    }

    /// <summary>
    /// Configuration options for Raft consensus.
    /// </summary>
    public class RaftConfiguration
    {
        /// <summary>
        /// Minimum election timeout in milliseconds (default: 300).
        /// </summary>
        public int ElectionTimeoutMinMs { get; set; } = 300;

        /// <summary>
        /// Maximum election timeout in milliseconds (default: 500).
        /// </summary>
        public int ElectionTimeoutMaxMs { get; set; } = 500;

        /// <summary>
        /// Heartbeat interval in milliseconds (default: 150).
        /// </summary>
        public int HeartbeatIntervalMs { get; set; } = 150;

        /// <summary>
        /// Maximum number of entries per append request (default: 100).
        /// </summary>
        public int MaxEntriesPerAppend { get; set; } = 100;

        /// <summary>
        /// Gets the randomized election timeout.
        /// </summary>
        public TimeSpan GetElectionTimeout()
        {
            var ms = System.Security.Cryptography.RandomNumberGenerator.GetInt32(ElectionTimeoutMinMs, ElectionTimeoutMaxMs);
            return TimeSpan.FromMilliseconds(ms);
        }
    }

    /// <summary>
    /// A single entry in the Raft log.
    /// </summary>
    public class RaftLogEntry
    {
        /// <summary>
        /// The term when this entry was received by the leader.
        /// </summary>
        public long Term { get; set; }

        /// <summary>
        /// The index of this entry in the log (1-based).
        /// </summary>
        public long Index { get; set; }

        /// <summary>
        /// The command/data to be applied to the state machine.
        /// </summary>
        public byte[] Data { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// The type of operation.
        /// </summary>
        public RaftOperationType OperationType { get; set; } = RaftOperationType.Command;

        /// <summary>
        /// When this entry was created.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Creates a new log entry.
        /// </summary>
        public static RaftLogEntry Create(long term, long index, byte[] data, RaftOperationType type = RaftOperationType.Command)
        {
            return new RaftLogEntry
            {
                Term = term,
                Index = index,
                Data = data,
                OperationType = type
            };
        }
    }

    /// <summary>
    /// Types of Raft operations.
    /// </summary>
    public enum RaftOperationType
    {
        /// <summary>
        /// Normal command from client.
        /// </summary>
        Command,

        /// <summary>
        /// Configuration change (add/remove node).
        /// </summary>
        ConfigurationChange,

        /// <summary>
        /// No-op entry from new leader.
        /// </summary>
        NoOp
    }

    /// <summary>
    /// Request for a vote during leader election.
    /// </summary>
    public class VoteRequest
    {
        /// <summary>
        /// Candidate's term.
        /// </summary>
        public long Term { get; set; }

        /// <summary>
        /// Candidate's node ID.
        /// </summary>
        public required string CandidateId { get; set; }

        /// <summary>
        /// Index of candidate's last log entry.
        /// </summary>
        public long LastLogIndex { get; set; }

        /// <summary>
        /// Term of candidate's last log entry.
        /// </summary>
        public long LastLogTerm { get; set; }
    }

    /// <summary>
    /// Response to a vote request.
    /// </summary>
    public class VoteResponse
    {
        /// <summary>
        /// Current term of the voter.
        /// </summary>
        public long Term { get; set; }

        /// <summary>
        /// Whether the vote was granted.
        /// </summary>
        public bool VoteGranted { get; set; }

        /// <summary>
        /// ID of the voter.
        /// </summary>
        public string? VoterId { get; set; }

        /// <summary>
        /// Creates a granted response.
        /// </summary>
        public static VoteResponse Granted(long term, string voterId)
        {
            return new VoteResponse { Term = term, VoteGranted = true, VoterId = voterId };
        }

        /// <summary>
        /// Creates a denied response.
        /// </summary>
        public static VoteResponse Denied(long term, string voterId)
        {
            return new VoteResponse { Term = term, VoteGranted = false, VoterId = voterId };
        }
    }

    /// <summary>
    /// Request to append entries to the log (used for heartbeat and replication).
    /// </summary>
    public class AppendRequest
    {
        /// <summary>
        /// Leader's term.
        /// </summary>
        public long Term { get; set; }

        /// <summary>
        /// Leader's node ID.
        /// </summary>
        public required string LeaderId { get; set; }

        /// <summary>
        /// Index of log entry immediately preceding new ones.
        /// </summary>
        public long PrevLogIndex { get; set; }

        /// <summary>
        /// Term of prevLogIndex entry.
        /// </summary>
        public long PrevLogTerm { get; set; }

        /// <summary>
        /// Log entries to store (empty for heartbeat).
        /// </summary>
        public List<RaftLogEntry> Entries { get; set; } = new();

        /// <summary>
        /// Leader's commit index.
        /// </summary>
        public long LeaderCommit { get; set; }

        /// <summary>
        /// Whether this is a heartbeat (no entries).
        /// </summary>
        public bool IsHeartbeat => Entries.Count == 0;
    }

    /// <summary>
    /// Response to an append entries request.
    /// </summary>
    public class AppendResponse
    {
        /// <summary>
        /// Current term of the follower.
        /// </summary>
        public long Term { get; set; }

        /// <summary>
        /// Whether the append was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// ID of the responder.
        /// </summary>
        public string? ResponderId { get; set; }

        /// <summary>
        /// Conflicting index hint for optimization.
        /// </summary>
        public long ConflictIndex { get; set; }

        /// <summary>
        /// Creates a successful response.
        /// </summary>
        public static AppendResponse CreateSuccess(long term, string responderId)
        {
            return new AppendResponse { Term = term, Success = true, ResponderId = responderId };
        }

        /// <summary>
        /// Creates a failed response.
        /// </summary>
        public static AppendResponse CreateFailure(long term, string responderId, long conflictIndex = 0)
        {
            return new AppendResponse { Term = term, Success = false, ResponderId = responderId, ConflictIndex = conflictIndex };
        }
    }

    /// <summary>
    /// Result of proposing an entry.
    /// </summary>
    public class ProposeResult
    {
        /// <summary>
        /// Whether the proposal was accepted.
        /// </summary>
        public bool Accepted { get; set; }

        /// <summary>
        /// Error message if rejected.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// The log entry index if accepted.
        /// </summary>
        public long LogIndex { get; set; }

        /// <summary>
        /// The term of the entry.
        /// </summary>
        public long Term { get; set; }

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        public static ProposeResult Success(long index, long term)
        {
            return new ProposeResult { Accepted = true, LogIndex = index, Term = term };
        }

        /// <summary>
        /// Creates a failed result.
        /// </summary>
        public static ProposeResult Failure(string error)
        {
            return new ProposeResult { Accepted = false, ErrorMessage = error };
        }
    }

    /// <summary>
    /// Statistics about Raft state.
    /// </summary>
    public class RaftStatistics
    {
        /// <summary>
        /// Current role.
        /// </summary>
        public RaftRole Role { get; set; }

        /// <summary>
        /// Current term.
        /// </summary>
        public long CurrentTerm { get; set; }

        /// <summary>
        /// Current leader ID.
        /// </summary>
        public string? LeaderId { get; set; }

        /// <summary>
        /// Last log index.
        /// </summary>
        public long LastLogIndex { get; set; }

        /// <summary>
        /// Last log term.
        /// </summary>
        public long LastLogTerm { get; set; }

        /// <summary>
        /// Commit index.
        /// </summary>
        public long CommitIndex { get; set; }

        /// <summary>
        /// Last applied index.
        /// </summary>
        public long LastApplied { get; set; }

        /// <summary>
        /// Total log entries.
        /// </summary>
        public int LogEntryCount { get; set; }

        /// <summary>
        /// Number of elections this node has won.
        /// </summary>
        public int ElectionsWon { get; set; }

        /// <summary>
        /// Number of votes cast for other nodes.
        /// </summary>
        public int VotesCast { get; set; }

        /// <summary>
        /// Number of entries committed.
        /// </summary>
        public long EntriesCommitted { get; set; }

        /// <summary>
        /// When the node started.
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// When the current role was assumed.
        /// </summary>
        public DateTime RoleStartTime { get; set; }
    }

    /// <summary>
    /// Event args for role changed event.
    /// </summary>
    public class RoleChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Previous role.
        /// </summary>
        public RaftRole PreviousRole { get; set; }

        /// <summary>
        /// New role.
        /// </summary>
        public RaftRole NewRole { get; set; }

        /// <summary>
        /// Term when the change occurred.
        /// </summary>
        public long Term { get; set; }

        /// <summary>
        /// When the change occurred.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Event args for log committed event.
    /// </summary>
    public class LogCommittedEventArgs : EventArgs
    {
        /// <summary>
        /// The committed entry.
        /// </summary>
        public required RaftLogEntry Entry { get; set; }

        /// <summary>
        /// The commit index.
        /// </summary>
        public long CommitIndex { get; set; }

        /// <summary>
        /// When the commit occurred.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Interface for sending RPC requests to other nodes.
    /// </summary>
    public interface IRaftRpcClient
    {
        /// <summary>
        /// Sends a vote request to a node.
        /// </summary>
        Task<VoteResponse> RequestVoteAsync(string targetNodeId, VoteRequest request, CancellationToken ct = default);

        /// <summary>
        /// Sends an append entries request to a node.
        /// </summary>
        Task<AppendResponse> AppendEntriesAsync(string targetNodeId, AppendRequest request, CancellationToken ct = default);
    }

    /// <summary>
    /// Implementation of Raft consensus protocol.
    /// </summary>
    public class RaftConsensus : IRaftConsensus
    {
        private readonly string _localNodeId;
        private readonly RaftConfiguration _config;
        private readonly IClusterManager _clusterManager;
        private readonly IRaftRpcClient _rpcClient;
        private readonly object _stateLock = new();

        // Persistent state
        private long _currentTerm = 0;
        private string? _votedFor = null;
        private readonly List<RaftLogEntry> _log = new();

        // Volatile state
        private RaftRole _currentRole = RaftRole.Follower;
        private string? _currentLeaderId;
        private long _commitIndex = 0;
        private long _lastApplied = 0;

        // Leader state (reinitialized after election)
        private readonly Dictionary<string, long> _nextIndex = new();
        private readonly Dictionary<string, long> _matchIndex = new();

        // Timers and cancellation
        private Timer? _electionTimer;
        private Timer? _heartbeatTimer;
        private CancellationTokenSource? _cts;

        // Statistics
        private DateTime _startTime = DateTime.UtcNow;
        private DateTime _roleStartTime = DateTime.UtcNow;
        private int _electionsWon = 0;
        private int _votesCast = 0;
        private long _entriesCommitted = 0;

        /// <inheritdoc/>
        public RaftRole CurrentRole => _currentRole;

        /// <inheritdoc/>
        public string? CurrentLeaderId => _currentLeaderId;

        /// <inheritdoc/>
        public long CurrentTerm => _currentTerm;

        /// <inheritdoc/>
        public string LocalNodeId => _localNodeId;

        /// <inheritdoc/>
        public event EventHandler<RoleChangedEventArgs>? RoleChanged;

        /// <inheritdoc/>
        public event EventHandler<LeaderChangedEventArgs>? LeaderChanged;

        /// <inheritdoc/>
        public event EventHandler<LogCommittedEventArgs>? LogCommitted;

        /// <summary>
        /// Creates a new Raft consensus instance.
        /// </summary>
        public RaftConsensus(
            string localNodeId,
            RaftConfiguration config,
            IClusterManager clusterManager,
            IRaftRpcClient rpcClient)
        {
            _localNodeId = localNodeId ?? throw new ArgumentNullException(nameof(localNodeId));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _clusterManager = clusterManager ?? throw new ArgumentNullException(nameof(clusterManager));
            _rpcClient = rpcClient ?? throw new ArgumentNullException(nameof(rpcClient));

            // Add a no-op entry at index 0 to simplify boundary checks
            _log.Add(RaftLogEntry.Create(0, 0, Array.Empty<byte>(), RaftOperationType.NoOp));
        }

        /// <inheritdoc/>
        public Task StartAsync(CancellationToken ct = default)
        {
            if (_cts != null)
                throw new InvalidOperationException("Raft consensus is already started.");

            _cts = new CancellationTokenSource();
            _startTime = DateTime.UtcNow;

            // Start as follower with election timer
            TransitionToRole(RaftRole.Follower);
            ResetElectionTimer();

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken ct = default)
        {
            _cts?.Cancel();
            _electionTimer?.Dispose();
            _heartbeatTimer?.Dispose();
            _cts?.Dispose();
            _cts = null;

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task<ProposeResult> ProposeAsync(RaftLogEntry entry, CancellationToken ct = default)
        {
            if (_currentRole != RaftRole.Leader)
            {
                return ProposeResult.Failure("Not the leader. Only the leader can propose entries.");
            }

            lock (_stateLock)
            {
                entry.Term = _currentTerm;
                entry.Index = _log.Count;
                _log.Add(entry);
            }

            // Replicate to followers
            await ReplicateToFollowersAsync(ct);

            return ProposeResult.Success(entry.Index, entry.Term);
        }

        /// <inheritdoc/>
        public Task<VoteResponse> HandleVoteRequestAsync(VoteRequest request, CancellationToken ct = default)
        {
            lock (_stateLock)
            {
                // If request term is higher, update our term and become follower
                if (request.Term > _currentTerm)
                {
                    UpdateTerm(request.Term);
                    TransitionToRole(RaftRole.Follower);
                    _votedFor = null;
                }

                // Reject if request term is lower
                if (request.Term < _currentTerm)
                {
                    return Task.FromResult(VoteResponse.Denied(_currentTerm, _localNodeId));
                }

                // Check if candidate's log is at least as up-to-date as ours
                var lastLogIndex = GetLastLogIndex();
                var lastLogTerm = GetLastLogTerm();

                bool logIsUpToDate = request.LastLogTerm > lastLogTerm ||
                                    (request.LastLogTerm == lastLogTerm && request.LastLogIndex >= lastLogIndex);

                // Grant vote if we haven't voted yet or already voted for this candidate
                if (logIsUpToDate && (_votedFor == null || _votedFor == request.CandidateId))
                {
                    _votedFor = request.CandidateId;
                    _votesCast++;
                    ResetElectionTimer();
                    return Task.FromResult(VoteResponse.Granted(_currentTerm, _localNodeId));
                }

                return Task.FromResult(VoteResponse.Denied(_currentTerm, _localNodeId));
            }
        }

        /// <inheritdoc/>
        public Task<AppendResponse> HandleAppendEntriesAsync(AppendRequest request, CancellationToken ct = default)
        {
            lock (_stateLock)
            {
                // If request term is higher, update our term and become follower
                if (request.Term > _currentTerm)
                {
                    UpdateTerm(request.Term);
                    TransitionToRole(RaftRole.Follower);
                }

                // Reject if request term is lower
                if (request.Term < _currentTerm)
                {
                    return Task.FromResult(AppendResponse.CreateFailure(_currentTerm, _localNodeId));
                }

                // Valid append from leader, reset election timer
                ResetElectionTimer();
                _currentLeaderId = request.LeaderId;

                // If we're not a follower, become one
                if (_currentRole != RaftRole.Follower)
                {
                    TransitionToRole(RaftRole.Follower);
                }

                // Check if we have the previous log entry
                if (request.PrevLogIndex > 0)
                {
                    if (request.PrevLogIndex >= _log.Count)
                    {
                        return Task.FromResult(AppendResponse.CreateFailure(_currentTerm, _localNodeId, _log.Count));
                    }

                    if (_log[(int)request.PrevLogIndex].Term != request.PrevLogTerm)
                    {
                        // Find conflicting index
                        long conflictIndex = request.PrevLogIndex;
                        while (conflictIndex > 0 && _log[(int)conflictIndex].Term == _log[(int)request.PrevLogIndex].Term)
                        {
                            conflictIndex--;
                        }
                        return Task.FromResult(AppendResponse.CreateFailure(_currentTerm, _localNodeId, conflictIndex + 1));
                    }
                }

                // Process entries
                if (request.Entries.Count > 0)
                {
                    // Remove any conflicting entries and append new ones
                    int insertIndex = (int)request.PrevLogIndex + 1;

                    foreach (var entry in request.Entries)
                    {
                        if (insertIndex < _log.Count)
                        {
                            if (_log[insertIndex].Term != entry.Term)
                            {
                                // Remove conflicting entry and all after it
                                _log.RemoveRange(insertIndex, _log.Count - insertIndex);
                                _log.Add(entry);
                            }
                        }
                        else
                        {
                            _log.Add(entry);
                        }
                        insertIndex++;
                    }
                }

                // Update commit index
                if (request.LeaderCommit > _commitIndex)
                {
                    _commitIndex = Math.Min(request.LeaderCommit, GetLastLogIndex());
                    ApplyCommittedEntries();
                }

                return Task.FromResult(AppendResponse.CreateSuccess(_currentTerm, _localNodeId));
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<RaftLogEntry> GetLogEntries()
        {
            lock (_stateLock)
            {
                return _log.ToList().AsReadOnly();
            }
        }

        /// <inheritdoc/>
        public RaftStatistics GetStatistics()
        {
            lock (_stateLock)
            {
                return new RaftStatistics
                {
                    Role = _currentRole,
                    CurrentTerm = _currentTerm,
                    LeaderId = _currentLeaderId,
                    LastLogIndex = GetLastLogIndex(),
                    LastLogTerm = GetLastLogTerm(),
                    CommitIndex = _commitIndex,
                    LastApplied = _lastApplied,
                    LogEntryCount = _log.Count - 1, // Exclude the no-op entry
                    ElectionsWon = _electionsWon,
                    VotesCast = _votesCast,
                    EntriesCommitted = _entriesCommitted,
                    StartTime = _startTime,
                    RoleStartTime = _roleStartTime
                };
            }
        }

        private void UpdateTerm(long newTerm)
        {
            _currentTerm = newTerm;
            _votedFor = null;
        }

        private void TransitionToRole(RaftRole newRole)
        {
            if (_currentRole == newRole)
                return;

            var previousRole = _currentRole;
            _currentRole = newRole;
            _roleStartTime = DateTime.UtcNow;

            // Stop existing timers
            _electionTimer?.Dispose();
            _heartbeatTimer?.Dispose();
            _electionTimer = null;
            _heartbeatTimer = null;

            switch (newRole)
            {
                case RaftRole.Follower:
                    _currentLeaderId = null;
                    ResetElectionTimer();
                    break;

                case RaftRole.Candidate:
                    StartElection();
                    break;

                case RaftRole.Leader:
                    _currentLeaderId = _localNodeId;
                    _electionsWon++;
                    InitializeLeaderState();
                    StartHeartbeatTimer();
                    break;
            }

            RoleChanged?.Invoke(this, new RoleChangedEventArgs
            {
                PreviousRole = previousRole,
                NewRole = newRole,
                Term = _currentTerm
            });

            if (newRole == RaftRole.Leader)
            {
                LeaderChanged?.Invoke(this, new LeaderChangedEventArgs
                {
                    NewLeader = new NodeInfo { NodeId = _localNodeId, Host = "localhost", P2PPort = 0 },
                    Term = _currentTerm
                });
            }
        }

        private void ResetElectionTimer()
        {
            _electionTimer?.Dispose();

            var timeout = _config.GetElectionTimeout();
            _electionTimer = new Timer(_ => OnElectionTimeout(), null, timeout, Timeout.InfiniteTimeSpan);
        }

        private void OnElectionTimeout()
        {
            if (_cts?.IsCancellationRequested ?? true)
                return;

            lock (_stateLock)
            {
                if (_currentRole == RaftRole.Leader)
                    return; // Leaders don't have election timeouts

                // Start election
                _currentTerm++;
                _votedFor = _localNodeId;
                TransitionToRole(RaftRole.Candidate);
            }
        }

        private void StartElection()
        {
            lock (_stateLock)
            {
                // Vote for self
                int voteCount = 1;
                var nodes = _clusterManager.GetNodesAsync().GetAwaiter().GetResult();
                var otherNodes = nodes.Where(n => n.NodeId != _localNodeId).ToList();

                if (otherNodes.Count == 0)
                {
                    // Single node cluster, become leader immediately
                    TransitionToRole(RaftRole.Leader);
                    return;
                }

                var lastLogIndex = GetLastLogIndex();
                var lastLogTerm = GetLastLogTerm();

                var request = new VoteRequest
                {
                    Term = _currentTerm,
                    CandidateId = _localNodeId,
                    LastLogIndex = lastLogIndex,
                    LastLogTerm = lastLogTerm
                };

                // Reset election timer for this term
                ResetElectionTimer();

                // Request votes from all other nodes
                var tasks = otherNodes.Select(node =>
                    Task.Run(async () =>
                    {
                        try
                        {
                            var response = await _rpcClient.RequestVoteAsync(node.NodeId, request, _cts?.Token ?? default);

                            lock (_stateLock)
                            {
                                if (response.Term > _currentTerm)
                                {
                                    UpdateTerm(response.Term);
                                    TransitionToRole(RaftRole.Follower);
                                    return;
                                }

                                if (_currentRole != RaftRole.Candidate || _currentTerm != request.Term)
                                    return;

                                if (response.VoteGranted)
                                {
                                    voteCount++;
                                    var majority = (nodes.Count / 2) + 1;

                                    if (voteCount >= majority)
                                    {
                                        TransitionToRole(RaftRole.Leader);
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Node unavailable, continue
                        }
                    })
                ).ToArray();

                // Wait for all vote requests to complete (with timeout)
                Task.WhenAll(tasks).Wait(TimeSpan.FromMilliseconds(_config.ElectionTimeoutMaxMs));
            }
        }

        private void InitializeLeaderState()
        {
            lock (_stateLock)
            {
                _nextIndex.Clear();
                _matchIndex.Clear();

                var nodes = _clusterManager.GetNodesAsync().GetAwaiter().GetResult();
                var nextIndex = GetLastLogIndex() + 1;

                foreach (var node in nodes)
                {
                    if (node.NodeId != _localNodeId)
                    {
                        _nextIndex[node.NodeId] = nextIndex;
                        _matchIndex[node.NodeId] = 0;
                    }
                }

                // Append a no-op entry to establish leadership
                var noOpEntry = RaftLogEntry.Create(_currentTerm, _log.Count, Array.Empty<byte>(), RaftOperationType.NoOp);
                _log.Add(noOpEntry);
            }
        }

        private void StartHeartbeatTimer()
        {
            _heartbeatTimer?.Dispose();
            var interval = TimeSpan.FromMilliseconds(_config.HeartbeatIntervalMs);
            _heartbeatTimer = new Timer(_ => SendHeartbeats(), null, TimeSpan.Zero, interval);
        }

        private void SendHeartbeats()
        {
            if (_cts?.IsCancellationRequested ?? true)
                return;

            if (_currentRole != RaftRole.Leader)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await ReplicateToFollowersAsync(_cts?.Token ?? default);
                }
                catch
                {
                    // Ignore errors during heartbeat
                }
            });
        }

        private async Task ReplicateToFollowersAsync(CancellationToken ct)
        {
            if (_currentRole != RaftRole.Leader)
                return;

            var nodes = await _clusterManager.GetNodesAsync(ct);
            var otherNodes = nodes.Where(n => n.NodeId != _localNodeId).ToList();

            if (otherNodes.Count == 0)
            {
                // Single node cluster, commit immediately
                lock (_stateLock)
                {
                    _commitIndex = GetLastLogIndex();
                    ApplyCommittedEntries();
                }
                return;
            }

            var tasks = otherNodes.Select(node => ReplicateToNodeAsync(node.NodeId, ct)).ToArray();
            await Task.WhenAll(tasks);

            // Check if we can advance commit index
            lock (_stateLock)
            {
                var matchIndexes = _matchIndex.Values.ToList();
                matchIndexes.Add(GetLastLogIndex()); // Include leader's match index
                matchIndexes.Sort();

                var majorityIndex = matchIndexes[matchIndexes.Count / 2];

                if (majorityIndex > _commitIndex && _log[(int)majorityIndex].Term == _currentTerm)
                {
                    _commitIndex = majorityIndex;
                    ApplyCommittedEntries();
                }
            }
        }

        private async Task ReplicateToNodeAsync(string nodeId, CancellationToken ct)
        {
            try
            {
                long nextIdx;
                long matchIdx;

                lock (_stateLock)
                {
                    if (!_nextIndex.TryGetValue(nodeId, out nextIdx))
                        return;
                    _matchIndex.TryGetValue(nodeId, out matchIdx);
                }

                var prevLogIndex = nextIdx - 1;
                var prevLogTerm = prevLogIndex > 0 && prevLogIndex < _log.Count
                    ? _log[(int)prevLogIndex].Term
                    : 0;

                var entries = new List<RaftLogEntry>();
                lock (_stateLock)
                {
                    for (long i = nextIdx; i < _log.Count && entries.Count < _config.MaxEntriesPerAppend; i++)
                    {
                        entries.Add(_log[(int)i]);
                    }
                }

                var request = new AppendRequest
                {
                    Term = _currentTerm,
                    LeaderId = _localNodeId,
                    PrevLogIndex = prevLogIndex,
                    PrevLogTerm = prevLogTerm,
                    Entries = entries,
                    LeaderCommit = _commitIndex
                };

                var response = await _rpcClient.AppendEntriesAsync(nodeId, request, ct);

                lock (_stateLock)
                {
                    if (response.Term > _currentTerm)
                    {
                        UpdateTerm(response.Term);
                        TransitionToRole(RaftRole.Follower);
                        return;
                    }

                    if (_currentRole != RaftRole.Leader)
                        return;

                    if (response.Success)
                    {
                        _nextIndex[nodeId] = nextIdx + entries.Count;
                        _matchIndex[nodeId] = nextIdx + entries.Count - 1;
                    }
                    else
                    {
                        // Decrement next index and retry
                        _nextIndex[nodeId] = response.ConflictIndex > 0 ? response.ConflictIndex : Math.Max(1, nextIdx - 1);
                    }
                }
            }
            catch
            {
                // Node unavailable, will retry on next heartbeat
            }
        }

        private void ApplyCommittedEntries()
        {
            while (_lastApplied < _commitIndex)
            {
                _lastApplied++;
                var entry = _log[(int)_lastApplied];
                _entriesCommitted++;

                LogCommitted?.Invoke(this, new LogCommittedEventArgs
                {
                    Entry = entry,
                    CommitIndex = _commitIndex
                });
            }
        }

        private long GetLastLogIndex()
        {
            return _log.Count - 1;
        }

        private long GetLastLogTerm()
        {
            var lastIndex = GetLastLogIndex();
            return lastIndex >= 0 ? _log[(int)lastIndex].Term : 0;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
        }
    }
}
