// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Core.Models;
using AdvGenNoSqlServer.Network;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using System.Text.Json;

namespace AdvGenNoSqlServer.Benchmarks;

[SimpleJob(RuntimeMoniker.Net90, launchCount: 1, warmupCount: 3, iterationCount: 5)]
[MemoryDiagnoser]
[RankColumn]
public class SerializationBenchmarks
{
    private Document _smallDocument = null!;
    private Document _mediumDocument = null!;
    private Document _largeDocument = null!;
    private byte[] _smallDocumentBytes = null!;
    private byte[] _mediumDocumentBytes = null!;
    private byte[] _largeDocumentBytes = null!;
    private MessageProtocol _protocol = null!;

    [GlobalSetup]
    public void Setup()
    {
        _protocol = new MessageProtocol();

        _smallDocument = new Document
        {
            Id = "123",
            Data = new Dictionary<string, object> { ["name"] = "Test" }
        };

        _mediumDocument = new Document
        {
            Id = "456",
            Data = new Dictionary<string, object>
            {
                ["name"] = "Test User",
                ["email"] = "test@example.com",
                ["age"] = 30,
                ["active"] = true,
                ["score"] = 95.5,
                ["tags"] = new List<string> { "tag1", "tag2", "tag3" },
                ["metadata"] = new Dictionary<string, object>
                {
                    ["created"] = DateTime.UtcNow,
                    ["version"] = 1
                }
            }
        };

        var largeData = new List<Dictionary<string, object>>();
        for (int i = 0; i < 100; i++)
        {
            largeData.Add(new Dictionary<string, object>
            {
                ["id"] = $"doc_{i}",
                ["index"] = i,
                ["data"] = new string('x', 100),
                ["field1"] = i * 10,
                ["field2"] = $"value_{i}",
                ["field3"] = i % 2 == 0
            });
        }
        _largeDocument = new Document
        {
            Id = "large-doc",
            Data = new Dictionary<string, object>
            {
                ["items"] = largeData,
                ["count"] = largeData.Count
            }
        };

        _smallDocumentBytes = JsonSerializer.SerializeToUtf8Bytes(_smallDocument.Data);
        _mediumDocumentBytes = JsonSerializer.SerializeToUtf8Bytes(_mediumDocument.Data);
        _largeDocumentBytes = JsonSerializer.SerializeToUtf8Bytes(_largeDocument.Data);
    }

    [Benchmark]
    public byte[] SerializeSmallDocument()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_smallDocument.Data);
    }

    [Benchmark]
    public byte[] SerializeMediumDocument()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_mediumDocument.Data);
    }

    [Benchmark]
    public byte[] SerializeLargeDocument()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_largeDocument.Data);
    }

    [Benchmark]
    public object? DeserializeSmallDocument()
    {
        return JsonSerializer.Deserialize<object>(_smallDocumentBytes);
    }

    [Benchmark]
    public object? DeserializeMediumDocument()
    {
        return JsonSerializer.Deserialize<object>(_mediumDocumentBytes);
    }

    [Benchmark]
    public object? DeserializeLargeDocument()
    {
        return JsonSerializer.Deserialize<object>(_largeDocumentBytes);
    }

    [Benchmark]
    public byte[] SerializeMessage()
    {
        var message = new NoSqlMessage
        {
            MessageType = MessageType.Command,
            Payload = _mediumDocumentBytes,
            PayloadLength = _mediumDocumentBytes.Length
        };
        return _protocol.Serialize(message);
    }

    [Benchmark]
    public NoSqlMessage DeserializeMessage()
    {
        var message = new NoSqlMessage
        {
            MessageType = MessageType.Command,
            Payload = _mediumDocumentBytes,
            PayloadLength = _mediumDocumentBytes.Length
        };
        var serialized = _protocol.Serialize(message);
        return _protocol.Deserialize(serialized);
    }
}
