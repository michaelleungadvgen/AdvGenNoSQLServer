// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Linq.Expressions;
using AdvGenNoSqlServer.Embedded.Typed;
using AdvGenNoSqlServer.Query.Models;
using Xunit;

namespace AdvGenNoSqlServer.Embedded.Tests;

public class ExpressionTranslatorTests
{
    private sealed class Item
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public bool Active { get; set; }
        public string Category { get; set; } = "";
    }

    private static QueryFilter? T(Expression<Func<Item, bool>> p) => ExpressionTranslator.TryTranslate(p);

    [Fact]
    public void Equality_ProducesEqCondition()
    {
        var f = T(x => x.Name == "Alice");
        Assert.NotNull(f);
        Assert.Equal("Alice", f!.Conditions["Name"]);
    }

    [Fact]
    public void GreaterThan_ProducesGtOperator()
    {
        var f = T(x => x.Age > 30);
        Assert.NotNull(f);
        var cond = Assert.IsType<Dictionary<string, object>>(f!.Conditions["Age"]);
        Assert.Equal(30L, cond["$gt"]);
    }

    [Fact]
    public void ReversedComparison_FlipsOperator()
    {
        var f = T(x => 30 < x.Age); // == Age > 30
        Assert.NotNull(f);
        var cond = Assert.IsType<Dictionary<string, object>>(f!.Conditions["Age"]);
        Assert.Equal(30L, cond["$gt"]);
    }

    [Fact]
    public void ClosureCapture_Evaluated()
    {
        int min = 18;
        var f = T(x => x.Age >= min);
        Assert.NotNull(f);
        var cond = Assert.IsType<Dictionary<string, object>>(f!.Conditions["Age"]);
        Assert.Equal(18L, cond["$gte"]);
    }

    [Fact]
    public void AndAlso_ProducesAnd()
    {
        var f = T(x => x.Age > 20 && x.Category == "A");
        Assert.NotNull(f);
        Assert.True(f!.Conditions.ContainsKey("$and"));
    }

    [Fact]
    public void OrElse_ProducesOr()
    {
        var f = T(x => x.Category == "A" || x.Category == "B");
        Assert.NotNull(f);
        Assert.True(f!.Conditions.ContainsKey("$or"));
    }

    [Fact]
    public void BareBool_ProducesEqTrue()
    {
        var f = T(x => x.Active);
        Assert.NotNull(f);
        Assert.Equal(true, f!.Conditions["Active"]);
    }

    [Fact]
    public void NegatedBool_ProducesEqFalse()
    {
        var f = T(x => !x.Active);
        Assert.NotNull(f);
        Assert.Equal(false, f!.Conditions["Active"]);
    }

    [Fact]
    public void ListContains_ProducesIn()
    {
        var cats = new List<string> { "A", "B" };
        var f = T(x => cats.Contains(x.Category));
        Assert.NotNull(f);
        var cond = Assert.IsType<Dictionary<string, object>>(f!.Conditions["Category"]);
        Assert.True(cond.ContainsKey("$in"));
    }

    [Fact]
    public void UnsupportedMethodCall_ReturnsNull()
    {
        Assert.Null(T(x => x.Name.ToLower() == "a"));
        Assert.Null(T(x => x.Name.Contains("z")));       // string methods deliberately not translated
        Assert.Null(T(x => x.Name.StartsWith("z")));
    }

    [Fact]
    public void Arithmetic_ReturnsNull()
    {
        Assert.Null(T(x => x.Age + 1 > 5));
    }
}
