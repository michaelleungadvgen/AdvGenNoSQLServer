// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AdvGenNoSqlServer.Core.Clustering;
using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Core.Transactions;
using Xunit;

namespace AdvGenNoSqlServer.Tests
{
    /// <summary>
    /// Unit tests for the full synchronization path (snapshot + trailing event replay).
    /// </summary>
    public class ReplicationFullSyncTests
    {
        #region Full Sync Flow Tests

        [Fact]
        public async Task RequestFullSync_SnapshotAndTrailingEvents_FollowerMatchesLeader()
        {
            using var leader = CreateManager("leader1");
            using var follower = CreateManager("follower1");
            var leaderStore = new InMemoryStore();
            var followerStore = new InMemoryStore();

            var snapshot = CreateSnapshot("leader1", 5,
                ("users", Doc("doc1", "alice")),
                ("users", Doc("doc2", "bob")));
            var trailing = new List<ReplicationEvent>
            {
                ReplicationEvent.Insert("leader1", "users", Doc("doc3", "carol"), 6, 1),
                ReplicationEvent.Update("leader1", "users", Doc("doc1", "alice-v2"), Doc("doc1", "alice"), 7, 1),
                ReplicationEvent.Delete("leader1", "users", "doc2", Doc("doc2", "bob"), 8, 1)
            };

            // Build leader state: snapshot contents + trailing events
            await leaderStore.RestoreAsync(snapshot, CancellationToken.None);
            foreach (var evt in trailing)
                await leaderStore.ApplyAsync(evt, CancellationToken.None);

            leader.FullSyncProvider = new FakeFullSyncProvider(snapshot, trailing);
            follower.FullSyncTransport = new FakeFullSyncTransport((_, ct) => leader.CreateFullSyncDataAsync(ct));
            follower.RestoreSnapshotCallback = followerStore.RestoreAsync;
            follower.ApplyEventCallback = followerStore.ApplyAsync;

            await follower.RequestFullSyncAsync("leader1");

            Assert.NotNull(followerStore.RestoredSnapshot);
            Assert.Equal(5, followerStore.RestoredSnapshot!.SequenceNumber);
            Assert.Equal(new long[] { 6, 7, 8 }, followerStore.AppliedEvents.Select(e => e.SequenceNumber));
            AssertStoresEqual(leaderStore, followerStore);
        }

        [Fact]
        public async Task RequestFullSync_Completed_UpdatesSyncStatus()
        {
            using var leader = CreateManager("leader1");
            using var follower = CreateManager("follower1");
            var followerStore = new InMemoryStore();

            var snapshot = CreateSnapshot("leader1", 5, ("users", Doc("doc1", "alice")));
            var trailing = new List<ReplicationEvent>
            {
                ReplicationEvent.Insert("leader1", "users", Doc("doc2", "bob"), 6, 1),
                ReplicationEvent.Insert("leader1", "users", Doc("doc3", "carol"), 7, 1)
            };

            leader.FullSyncProvider = new FakeFullSyncProvider(snapshot, trailing);
            follower.FullSyncTransport = new FakeFullSyncTransport((_, ct) => leader.CreateFullSyncDataAsync(ct));
            follower.RestoreSnapshotCallback = followerStore.RestoreAsync;
            follower.ApplyEventCallback = followerStore.ApplyAsync;

            await follower.RequestFullSyncAsync("leader1");

            var status = await follower.GetSyncStatusAsync("leader1");
            Assert.True(status.IsSynchronized);
            Assert.Equal(7, status.LastSequenceNumber);
            Assert.Equal(0, status.PendingEvents);
            Assert.NotEqual(default, status.LastReplicationTime);
        }

        [Fact]
        public async Task RequestFullSync_RequestsFromSpecifiedNode()
        {
            using var leader = CreateManager("leader1");
            using var follower = CreateManager("follower1");
            var followerStore = new InMemoryStore();

            leader.FullSyncProvider = new FakeFullSyncProvider(CreateSnapshot("leader1", 3), new List<ReplicationEvent>());
            var transport = new FakeFullSyncTransport((_, ct) => leader.CreateFullSyncDataAsync(ct));
            follower.FullSyncTransport = transport;
            follower.RestoreSnapshotCallback = followerStore.RestoreAsync;

            await follower.RequestFullSyncAsync("leader1");

            Assert.Equal(new[] { "leader1" }, transport.RequestedNodes);
        }

        [Fact]
        public async Task RequestFullSync_NoTrailingEvents_SyncsToSnapshotPosition()
        {
            using var leader = CreateManager("leader1");
            using var follower = CreateManager("follower1");
            var followerStore = new InMemoryStore();

            var snapshot = CreateSnapshot("leader1", 42, ("users", Doc("doc1", "alice")));
            leader.FullSyncProvider = new FakeFullSyncProvider(snapshot, new List<ReplicationEvent>());
            follower.FullSyncTransport = new FakeFullSyncTransport((_, ct) => leader.CreateFullSyncDataAsync(ct));
            follower.RestoreSnapshotCallback = followerStore.RestoreAsync;
            follower.ApplyEventCallback = followerStore.ApplyAsync;

            await follower.RequestFullSyncAsync("leader1");

            var status = await follower.GetSyncStatusAsync("leader1");
            Assert.True(status.IsSynchronized);
            Assert.Equal(42, status.LastSequenceNumber);
            Assert.Empty(followerStore.AppliedEvents);
            Assert.True(followerStore.Collections["users"].ContainsKey("doc1"));
        }

        [Fact]
        public async Task RequestFullSync_OutOfOrderTrailingEvents_AppliedInSequenceOrder()
        {
            using var follower = CreateManager("follower1");
            var followerStore = new InMemoryStore();

            var snapshot = CreateSnapshot("leader1", 5, ("users", Doc("doc1", "v1")));
            var data = new FullSyncData
            {
                Snapshot = snapshot,
                TrailingEvents = new List<ReplicationEvent>
                {
                    ReplicationEvent.Update("leader1", "users", Doc("doc1", "v4"), Doc("doc1", "v3"), 8, 1),
                    ReplicationEvent.Update("leader1", "users", Doc("doc1", "v2"), Doc("doc1", "v1"), 6, 1),
                    ReplicationEvent.Update("leader1", "users", Doc("doc1", "v3"), Doc("doc1", "v2"), 7, 1)
                }
            };

            follower.FullSyncTransport = new FakeFullSyncTransport((_, _) => Task.FromResult(data));
            follower.RestoreSnapshotCallback = followerStore.RestoreAsync;
            follower.ApplyEventCallback = followerStore.ApplyAsync;

            await follower.RequestFullSyncAsync("leader1");

            Assert.Equal(new long[] { 6, 7, 8 }, followerStore.AppliedEvents.Select(e => e.SequenceNumber));
            Assert.Equal("v4", followerStore.Collections["users"]["doc1"].Data["name"]);
            var status = await follower.GetSyncStatusAsync("leader1");
            Assert.Equal(8, status.LastSequenceNumber);
        }

        #endregion

        #region Failure and Validation Tests

        [Fact]
        public async Task RequestFullSync_NoTransport_ThrowsInvalidOperationException()
        {
            using var follower = CreateManager("follower1");
            follower.RestoreSnapshotCallback = (_, _) => Task.CompletedTask;

            await Assert.ThrowsAsync<InvalidOperationException>(() => follower.RequestFullSyncAsync("leader1"));
        }

        [Fact]
        public async Task RequestFullSync_NoRestoreCallback_ThrowsInvalidOperationException()
        {
            using var follower = CreateManager("follower1");
            follower.FullSyncTransport = new FakeFullSyncTransport((_, _) =>
                Task.FromResult(new FullSyncData { Snapshot = CreateSnapshot("leader1", 1) }));

            await Assert.ThrowsAsync<InvalidOperationException>(() => follower.RequestFullSyncAsync("leader1"));
        }

        [Fact]
        public async Task RequestFullSync_EmptyNodeId_ThrowsArgumentException()
        {
            using var follower = CreateManager("follower1");

            await Assert.ThrowsAsync<ArgumentException>(() => follower.RequestFullSyncAsync(""));
        }

        [Fact]
        public async Task RequestFullSync_TransportFailure_ThrowsReplicationSyncExceptionAndStaysUnsynchronized()
        {
            using var follower = CreateManager("follower1");
            follower.RestoreSnapshotCallback = (_, _) => Task.CompletedTask;
            follower.FullSyncTransport = new FakeFullSyncTransport((_, _) =>
                throw new IOException("Connection lost"));

            await Assert.ThrowsAsync<ReplicationSyncException>(() => follower.RequestFullSyncAsync("leader1"));

            var status = await follower.GetSyncStatusAsync("leader1");
            Assert.False(status.IsSynchronized);
            Assert.Equal(long.MaxValue, status.PendingEvents);
            Assert.Equal(1, follower.Statistics.TotalFailures);
        }

        [Fact]
        public async Task RequestFullSync_OverlappingTrailingEvents_ThrowsAndDoesNotRestore()
        {
            using var follower = CreateManager("follower1");
            var followerStore = new InMemoryStore();

            var data = new FullSyncData
            {
                Snapshot = CreateSnapshot("leader1", 5),
                TrailingEvents = new List<ReplicationEvent>
                {
                    ReplicationEvent.Insert("leader1", "users", Doc("doc1", "alice"), 4, 1)
                }
            };

            follower.FullSyncTransport = new FakeFullSyncTransport((_, _) => Task.FromResult(data));
            follower.RestoreSnapshotCallback = followerStore.RestoreAsync;

            await Assert.ThrowsAsync<ReplicationSyncException>(() => follower.RequestFullSyncAsync("leader1"));

            Assert.Null(followerStore.RestoredSnapshot);
            var status = await follower.GetSyncStatusAsync("leader1");
            Assert.False(status.IsSynchronized);
        }

        [Fact]
        public async Task RequestFullSync_PreCancelled_ThrowsOperationCanceledException()
        {
            using var follower = CreateManager("follower1");
            follower.RestoreSnapshotCallback = (_, _) => Task.CompletedTask;
            follower.FullSyncTransport = new FakeFullSyncTransport((_, _) =>
                Task.FromResult(new FullSyncData { Snapshot = CreateSnapshot("leader1", 1) }));

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => follower.RequestFullSyncAsync("leader1", cts.Token));
        }

        [Fact]
        public async Task CreateFullSyncData_NoProvider_ThrowsInvalidOperationException()
        {
            using var leader = CreateManager("leader1");

            await Assert.ThrowsAsync<InvalidOperationException>(() => leader.CreateFullSyncDataAsync());
        }

        [Fact]
        public async Task CreateFullSyncData_EventsBeforeSnapshotPosition_Excluded()
        {
            using var leader = CreateManager("leader1");
            var snapshot = CreateSnapshot("leader1", 5, ("users", Doc("doc1", "alice")));
            var events = new List<ReplicationEvent>
            {
                ReplicationEvent.Insert("leader1", "users", Doc("doc1", "alice"), 3, 1),
                ReplicationEvent.Insert("leader1", "users", Doc("doc2", "bob"), 5, 1),
                ReplicationEvent.Insert("leader1", "users", Doc("doc3", "carol"), 6, 1),
                ReplicationEvent.Insert("leader1", "users", Doc("doc4", "dave"), 9, 1)
            };
            leader.FullSyncProvider = new FakeFullSyncProvider(snapshot, events);

            var data = await leader.CreateFullSyncDataAsync();

            Assert.Equal(new long[] { 6, 9 }, data.TrailingEvents.Select(e => e.SequenceNumber));
            Assert.Equal(9, data.LastSequenceNumber);
        }

        #endregion

        #region WAL-backed Provider Tests

        [Fact]
        public async Task RequestFullSync_WalBackedLeader_ReplaysTrailingWalEntries()
        {
            var walDirectory = Path.Combine(Path.GetTempPath(), "fullsync_wal_" + Guid.NewGuid().ToString("N"));
            try
            {
                var walOptions = new WalOptions
                {
                    LogDirectory = walDirectory,
                    CheckpointInterval = TimeSpan.Zero,
                    ForceSync = false
                };
                using var wal = new WriteAheadLog(walOptions);

                var doc1V1 = Doc("doc1", "alice");
                var doc1V2 = Doc("doc1", "alice-v2");
                var doc2 = Doc("doc2", "bob");

                await wal.AppendBeginTransactionAsync("txn1");
                await wal.AppendInsertAsync("txn1", "users", doc1V1);
                var snapshotLsn = await wal.AppendInsertAsync("txn1", "users", doc2);
                await wal.AppendCommitAsync("txn1");
                var updateLsn = await wal.AppendUpdateAsync("txn2", "users", doc1V1, doc1V2);
                var deleteLsn = await wal.AppendDeleteAsync("txn2", "users", doc2);
                await wal.FlushAsync();

                var snapshot = new ReplicationSnapshot
                {
                    SourceNodeId = "leader1",
                    SequenceNumber = snapshotLsn,
                    Term = 7,
                    Collections = new Dictionary<string, List<Document>>
                    {
                        ["users"] = new List<Document> { doc1V1, doc2 }
                    }
                };

                using var leader = CreateManager("leader1");
                using var follower = CreateManager("follower1");
                var followerStore = new InMemoryStore();

                leader.FullSyncProvider = new WalFullSyncProvider(
                    wal, _ => Task.FromResult(snapshot), "leader1", () => 7);
                follower.FullSyncTransport = new FakeFullSyncTransport((_, ct) => leader.CreateFullSyncDataAsync(ct));
                follower.RestoreSnapshotCallback = followerStore.RestoreAsync;
                follower.ApplyEventCallback = followerStore.ApplyAsync;

                await follower.RequestFullSyncAsync("leader1");

                // Transaction markers are skipped; only data operations are replayed
                Assert.Equal(new long[] { updateLsn, deleteLsn }, followerStore.AppliedEvents.Select(e => e.SequenceNumber));
                Assert.Equal(ReplicationEventType.Update, followerStore.AppliedEvents[0].Type);
                Assert.Equal(ReplicationEventType.Delete, followerStore.AppliedEvents[1].Type);
                Assert.All(followerStore.AppliedEvents, e => Assert.Equal(7, e.Term));

                // doc1 updated to v2, doc2 deleted (WAL round-trip yields JsonElement values)
                var users = followerStore.Collections["users"];
                Assert.Single(users);
                Assert.Equal("alice-v2", users["doc1"].Data["name"].ToString());

                var status = await follower.GetSyncStatusAsync("leader1");
                Assert.True(status.IsSynchronized);
                Assert.Equal(deleteLsn, status.LastSequenceNumber);
            }
            finally
            {
                if (Directory.Exists(walDirectory))
                    Directory.Delete(walDirectory, true);
            }
        }

        [Fact]
        public void WalFullSyncProvider_NullWal_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new WalFullSyncProvider(null!, _ => Task.FromResult(CreateSnapshot("n", 0)), "n"));
        }

        [Fact]
        public void WalFullSyncProvider_NullSnapshotFactory_ThrowsArgumentNullException()
        {
            var walDirectory = Path.Combine(Path.GetTempPath(), "fullsync_wal_" + Guid.NewGuid().ToString("N"));
            try
            {
                using var wal = new WriteAheadLog(new WalOptions { LogDirectory = walDirectory, CheckpointInterval = TimeSpan.Zero });
                Assert.Throws<ArgumentNullException>(() => new WalFullSyncProvider(wal, null!, "n"));
            }
            finally
            {
                if (Directory.Exists(walDirectory))
                    Directory.Delete(walDirectory, true);
            }
        }

        [Fact]
        public void WalFullSyncProvider_EmptySourceNodeId_ThrowsArgumentException()
        {
            var walDirectory = Path.Combine(Path.GetTempPath(), "fullsync_wal_" + Guid.NewGuid().ToString("N"));
            try
            {
                using var wal = new WriteAheadLog(new WalOptions { LogDirectory = walDirectory, CheckpointInterval = TimeSpan.Zero });
                Assert.Throws<ArgumentException>(() =>
                    new WalFullSyncProvider(wal, _ => Task.FromResult(CreateSnapshot("n", 0)), ""));
            }
            finally
            {
                if (Directory.Exists(walDirectory))
                    Directory.Delete(walDirectory, true);
            }
        }

        #endregion

        #region Message Tests

        [Fact]
        public void FullSyncMessages_HaveExpectedMessageTypes()
        {
            var request = new FullSyncRequestMessage { SenderId = "follower1", LastKnownSequenceNumber = 12 };
            var response = new FullSyncResponseMessage
            {
                SenderId = "leader1",
                RequestMessageId = request.MessageId,
                Success = true,
                Data = new FullSyncData { Snapshot = CreateSnapshot("leader1", 12) }
            };

            Assert.Equal(P2PMessageType.FullSyncRequest, request.MessageType);
            Assert.Equal(P2PMessageType.FullSyncResponse, response.MessageType);
            Assert.Equal(12, request.LastKnownSequenceNumber);
            Assert.Equal(request.MessageId, response.RequestMessageId);
            Assert.Equal(12, response.Data!.LastSequenceNumber);
        }

        #endregion

        #region Helpers

        private static ReplicationManager CreateManager(string localNodeId)
        {
            return new ReplicationManager(new FakeClusterManager(localNodeId));
        }

        private static Document Doc(string id, string name)
        {
            return new Document
            {
                Id = id,
                Data = new Dictionary<string, object> { ["name"] = name },
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Version = 1
            };
        }

        private static ReplicationSnapshot CreateSnapshot(string sourceNodeId, long sequenceNumber, params (string Collection, Document Document)[] documents)
        {
            var snapshot = new ReplicationSnapshot
            {
                SourceNodeId = sourceNodeId,
                SequenceNumber = sequenceNumber
            };
            foreach (var (collection, document) in documents)
            {
                if (!snapshot.Collections.TryGetValue(collection, out var list))
                {
                    list = new List<Document>();
                    snapshot.Collections[collection] = list;
                }
                list.Add(document);
            }
            return snapshot;
        }

        private static void AssertStoresEqual(InMemoryStore expected, InMemoryStore actual)
        {
            Assert.Equal(
                expected.Collections.Keys.OrderBy(k => k),
                actual.Collections.Keys.OrderBy(k => k));
            foreach (var collection in expected.Collections.Keys)
            {
                Assert.Equal(
                    expected.Collections[collection].Keys.OrderBy(k => k),
                    actual.Collections[collection].Keys.OrderBy(k => k));
                foreach (var id in expected.Collections[collection].Keys)
                {
                    Assert.Equal(
                        expected.Collections[collection][id].Data["name"],
                        actual.Collections[collection][id].Data["name"]);
                }
            }
        }

        #endregion

        #region Fakes

        private class InMemoryStore
        {
            public Dictionary<string, Dictionary<string, Document>> Collections { get; } = new();
            public List<ReplicationEvent> AppliedEvents { get; } = new();
            public ReplicationSnapshot? RestoredSnapshot { get; private set; }

            public Task RestoreAsync(ReplicationSnapshot snapshot, CancellationToken ct)
            {
                RestoredSnapshot = snapshot;
                Collections.Clear();
                foreach (var (collection, documents) in snapshot.Collections)
                {
                    Collections[collection] = documents.ToDictionary(d => d.Id, d => d);
                }
                return Task.CompletedTask;
            }

            public Task ApplyAsync(ReplicationEvent evt, CancellationToken ct)
            {
                AppliedEvents.Add(evt);
                switch (evt.Type)
                {
                    case ReplicationEventType.Insert:
                    case ReplicationEventType.Update:
                        if (!Collections.TryGetValue(evt.Collection, out var docs))
                        {
                            docs = new Dictionary<string, Document>();
                            Collections[evt.Collection] = docs;
                        }
                        docs[evt.DocumentId!] = evt.Document!;
                        break;
                    case ReplicationEventType.Delete:
                        if (Collections.TryGetValue(evt.Collection, out var existing))
                            existing.Remove(evt.DocumentId!);
                        break;
                    case ReplicationEventType.DropCollection:
                        Collections.Remove(evt.Collection);
                        break;
                }
                return Task.CompletedTask;
            }
        }

        private class FakeFullSyncProvider : IFullSyncProvider
        {
            private readonly ReplicationSnapshot _snapshot;
            private readonly List<ReplicationEvent> _events;

            public FakeFullSyncProvider(ReplicationSnapshot snapshot, List<ReplicationEvent> events)
            {
                _snapshot = snapshot;
                _events = events;
            }

            public Task<ReplicationSnapshot> CreateSnapshotAsync(CancellationToken ct = default)
            {
                return Task.FromResult(_snapshot);
            }

            public async IAsyncEnumerable<ReplicationEvent> GetEventsAfterAsync(
                long sequenceNumber,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                foreach (var evt in _events.Where(e => e.SequenceNumber > sequenceNumber).OrderBy(e => e.SequenceNumber))
                {
                    ct.ThrowIfCancellationRequested();
                    yield return evt;
                }
            }
        }

        private class FakeFullSyncTransport : IFullSyncTransport
        {
            private readonly Func<string, CancellationToken, Task<FullSyncData>> _handler;

            public FakeFullSyncTransport(Func<string, CancellationToken, Task<FullSyncData>> handler)
            {
                _handler = handler;
            }

            public List<string> RequestedNodes { get; } = new();

            public Task<FullSyncData> RequestFullSyncAsync(string nodeId, CancellationToken ct = default)
            {
                RequestedNodes.Add(nodeId);
                return _handler(nodeId, ct);
            }
        }

        private class FakeClusterManager : IClusterManager
        {
            private readonly List<NodeInfo> _nodes = new();

            public FakeClusterManager(string localNodeId)
            {
                LocalNode = new NodeIdentity
                {
                    NodeId = localNodeId,
                    ClusterId = "test-cluster",
                    Host = "localhost",
                    Port = 9090,
                    P2PPort = 9091,
                    State = NodeState.Active
                };
            }

            public NodeIdentity LocalNode { get; }
            public bool IsClusterMember => true;
            public bool IsLeader => true;

            public Task<ClusterInfo> GetClusterInfoAsync(CancellationToken ct = default)
            {
                return Task.FromResult(new ClusterInfo
                {
                    ClusterId = "test-cluster",
                    ClusterName = "Test Cluster",
                    Nodes = _nodes.ToList(),
                    Health = ClusterHealth.Healthy
                });
            }

            public Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken ct = default)
            {
                return Task.FromResult<IReadOnlyList<NodeInfo>>(_nodes.ToList());
            }

            public Task<NodeInfo?> GetNodeAsync(string nodeId, CancellationToken ct = default)
            {
                return Task.FromResult(_nodes.FirstOrDefault(n => n.NodeId == nodeId));
            }

            public Task<JoinResult> JoinClusterAsync(string seedNode, JoinOptions options, CancellationToken ct = default)
            {
                return Task.FromResult(new JoinResult { Success = true });
            }

            public Task<LeaveResult> LeaveClusterAsync(LeaveOptions options, CancellationToken ct = default)
            {
                return Task.FromResult(new LeaveResult { Success = true });
            }

            public Task<bool> RemoveNodeAsync(string nodeId, CancellationToken ct = default)
            {
                _nodes.RemoveAll(n => n.NodeId == nodeId);
                return Task.FromResult(true);
            }

            public Task<NodeInfo?> GetLeaderAsync(CancellationToken ct = default)
            {
                return Task.FromResult<NodeInfo?>(new NodeInfo { NodeId = LocalNode.NodeId, Host = "localhost", P2PPort = 9091 });
            }

            public Task<bool> RequestLeaderElectionAsync(CancellationToken ct = default)
            {
                return Task.FromResult(true);
            }

            public Task<bool> UpdateNodeStateAsync(NodeState newState, CancellationToken ct = default)
            {
                LocalNode.State = newState;
                return Task.FromResult(true);
            }

            public Task<JoinResult> CreateClusterAsync(string clusterName, CancellationToken ct = default)
            {
                return Task.FromResult(new JoinResult { Success = true });
            }

            public void Dispose()
            {
            }

            public event EventHandler<NodeJoinedEventArgs>? NodeJoined;
            public event EventHandler<NodeLeftEventArgs>? NodeLeft;
            public event EventHandler<LeaderChangedEventArgs>? LeaderChanged;
            public event EventHandler<NodeStateChangedEventArgs>? NodeStateChanged;
        }

        #endregion
    }
}