// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using AdvGenNoSqlServer.Embedded.Typed;
using Xunit;

namespace AdvGenNoSqlServer.Embedded.Tests;

public class DocumentMapperTests
{
    private sealed class Item
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public bool Active { get; set; }
        public DateTime Created { get; set; }
        public int? Optional { get; set; }
        public List<string> Tags { get; set; } = new();
        public Nested? Detail { get; set; }
    }

    private sealed class Nested
    {
        public string Label { get; set; } = "";
        public double Weight { get; set; }
    }

    private sealed class NoId
    {
        public string Name { get; set; } = "";
    }

    private sealed class CustomId
    {
        [EmbeddedId] public string Key { get; set; } = "";
        public string Name { get; set; } = "";
    }

    [Fact]
    public void RoundTrip_PreservesAllProperties()
    {
        var mapper = new DocumentMapper<Item>();
        var item = new Item
        {
            Id = "x1",
            Name = "Widget",
            Quantity = 7,
            Price = 123.45m,
            Active = true,
            Created = new DateTime(2026, 7, 15, 10, 30, 0, DateTimeKind.Utc),
            Optional = null,
            Tags = new() { "a", "b" },
            Detail = new Nested { Label = "deep", Weight = 2.5 }
        };
        var doc = mapper.ToDocument(item);
        Assert.Equal("x1", doc.Id);

        var back = mapper.ToEntity(doc);
        Assert.Equal("x1", back.Id);
        Assert.Equal("Widget", back.Name);
        Assert.Equal(7, back.Quantity);
        Assert.Equal(123.45m, back.Price);
        Assert.True(back.Active);
        Assert.Equal(item.Created, back.Created);
        Assert.Null(back.Optional);
        Assert.Equal(new[] { "a", "b" }, back.Tags);
        Assert.NotNull(back.Detail);
        Assert.Equal("deep", back.Detail!.Label);
        Assert.Equal(2.5, back.Detail.Weight);
    }

    [Fact]
    public void Id_NotStoredInData()
    {
        var mapper = new DocumentMapper<Item>();
        var doc = mapper.ToDocument(new Item { Id = "x", Name = "n" });
        Assert.False(doc.Data!.ContainsKey("Id"));
        Assert.True(doc.Data.ContainsKey("Name"));
    }

    [Fact]
    public void NoIdProperty_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new DocumentMapper<NoId>());
    }

    [Fact]
    public void CustomIdAttribute_Works()
    {
        var mapper = new DocumentMapper<CustomId>();
        var doc = mapper.ToDocument(new CustomId { Key = "k1", Name = "n" });
        Assert.Equal("k1", doc.Id);
        Assert.False(doc.Data!.ContainsKey("Key"));

        var back = mapper.ToEntity(doc);
        Assert.Equal("k1", back.Key);
        Assert.Equal("n", back.Name);
    }

    [Fact]
    public void SetId_WritesBack()
    {
        var mapper = new DocumentMapper<Item>();
        var item = new Item();
        mapper.SetId(item, "assigned");
        Assert.Equal("assigned", item.Id);
        Assert.Equal("assigned", mapper.GetId(item));
    }

    [Fact]
    public void Decimal_RoundTripsExactly()
    {
        var mapper = new DocumentMapper<Item>();
        var item = new Item { Id = "d", Price = 9999999.99m };
        var back = mapper.ToEntity(mapper.ToDocument(item));
        Assert.Equal(9999999.99m, back.Price);
    }
}
