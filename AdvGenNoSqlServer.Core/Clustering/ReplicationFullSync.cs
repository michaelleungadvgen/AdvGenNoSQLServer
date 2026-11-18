// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Transactions;

namespace AdvGenNoSqlServer.Core.Clustering
{
    /// <summary>
    /// Point-in-time snapshot of a node's replicated data.
    /// </summary>
    public class ReplicationSnapshot
    {
        /// <summary>
        /// ID of the node that produced the snapshot.
        /// </summary>
        public required string SourceNodeId { get; set; }

        /// <summary>
        /// WAL sequence number the snapshot was taken at.
        /// </summary>
        public long SequenceNumber { get; set; }

        /// <summary>
        /// Raft term at snapshot time.
        /// </summary>
        public long Term { get; set; }

        /// <summary>
        /// Timestamp when the snapshot was created.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// All documents keyed by collection name.
        /// </summary>
        public Dictionary<string, List<Document>> Collections { get; set; } = new();
    }

    /// <summary>
    /// Full synchronization payload: a snapshot plus the replication events after it.
    /// </summary>
    public class FullSyncData
    {
        /// <summary>
        /// The base snapshot to restore.
        /// </summary>
        public required ReplicationSnapshot Snapshot { get; set; }

        /// <summary>
        /// Events with sequence numbers greater than the snapshot position, in order.
        /// </summary>
        public List<ReplicationEvent> TrailingEvents { get; set; } = new();

        /// <summary>
        /// Sequence number reached after applying the snapshot and all trailing events.
        /// </summary>
        public long LastSequenceNumber => TrailingEvents.Count > 0
            ? TrailingEvents.Max(e => e.SequenceNumber)
            : Snapshot.SequenceNumber;
    }

    /// <summary>
    /// Leader-side source of snapshots and trailing replication-log events.
    /// </summary>
    public interface IFullSyncProvider
    {
        /// <summary>
        /// Creates a snapshot of the current local state.
        /// </summary>
        Task<ReplicationSnapshot> CreateSnapshotAsync(CancellationToken ct = default);

        /// <summary>
        /// Streams replication events with sequence numbers greater than the given position.
        /// </summary>
        IAsyncEnumerable<ReplicationEvent> GetEventsAfterAsync(long sequenceNumber, CancellationToken ct = default);
    }

    /// <summary>
    /// Transport used by a follower to fetch a full sync payload from a peer node.
    /// </summary>
    public interface IFullSyncTransport
    {
        /// <summary>
        /// Requests a snapshot plus trailing events from the specified node.
        /// </summary>
        Task<FullSyncData> RequestFullSyncAsync(string nodeId, CancellationToken ct = default);
    }

    /// <summary>
    /// Thrown when a full synchronization fails.
    /// </summary>
    public class ReplicationSyncException : Exception
    {
        /// <summary>
        /// Creates a new replication sync exception.
        /// </summary>
        public ReplicationSyncException(string message) : base(message)
        {
        }

        /// <summary>
        /// Creates a new replication sync exception with an inner exception.
        /// </summary>
        public ReplicationSyncException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Full-sync provider backed by the Write-Ahead Log for trailing event replay.
    /// </summary>
    public class WalFullSyncProvider : IFullSyncProvider
    {
        private readonly IWriteAheadLog _wal;
        private readonly Func<CancellationToken, Task<ReplicationSnapshot>> _snapshotFactory;
        private readonly string _sourceNodeId;
        private readonly Func<long> _termProvider;

        /// <summary>
        /// Creates a WAL-backed full-sync provider.
        /// </summary>
        /// <param name="wal">Write-ahead log to replay trailing events from.</param>
        /// <param name="snapshotFactory">Factory producing a snapshot at the current WAL position.</param>
        /// <param name="sourceNodeId">Local node ID stamped onto replayed events.</param>
        /// <param name="termProvider">Optional provider of the current Raft term.</param>
        public WalFullSyncProvider(
            IWriteAheadLog wal,
            Func<CancellationToken, Task<ReplicationSnapshot>> snapshotFactory,
            string sourceNodeId,
            Func<long>? termProvider = null)
        {
            _wal = wal ?? throw new ArgumentNullException(nameof(wal));
            _snapshotFactory = snapshotFactory ?? throw new ArgumentNullException(nameof(snapshotFactory));
            if (string.IsNullOrEmpty(sourceNodeId))
                throw new ArgumentException("Source node ID cannot be empty", nameof(sourceNodeId));
            _sourceNodeId = sourceNodeId;
            _termProvider = termProvider ?? (() => 0);
        }

        /// <inheritdoc/>
        public Task<ReplicationSnapshot> CreateSnapshotAsync(CancellationToken ct = default)
        {
            return _snapshotFactory(ct);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<ReplicationEvent> GetEventsAfterAsync(
            long sequenceNumber,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var entry in _wal.ReplayEntriesAsync(sequenceNumber + 1, ct))
            {
                if (entry.Lsn <= sequenceNumber)
                    continue;

                var evt = ConvertEntry(entry);
                if (evt != null)
                    yield return evt;
            }
        }

        private ReplicationEvent? ConvertEntry(WalLogEntry entry)
        {
            if (entry.CollectionName == null)
                return null;

            var term = _termProvider();
            switch (entry.OperationType)
            {
                case WalOperationType.Insert when entry.AfterImage != null:
                    return ReplicationEvent.Insert(_sourceNodeId, entry.CollectionName, entry.AfterImage, entry.Lsn, term);

                case WalOperationType.Update when entry.AfterImage != null && entry.BeforeImage != null:
                    return ReplicationEvent.Update(_sourceNodeId, entry.CollectionName, entry.AfterImage, entry.BeforeImage, entry.Lsn, term);

                case WalOperationType.Delete:
                    var previous = entry.BeforeImage ?? entry.AfterImage;
                    var documentId = entry.DocumentId ?? previous?.Id;
                    if (documentId == null || previous == null)
                        return null;
                    return ReplicationEvent.Delete(_sourceNodeId, entry.CollectionName, documentId, previous, entry.Lsn, term);

                default:
                    return null;
            }
        }
    }
}