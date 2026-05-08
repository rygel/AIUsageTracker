// <copyright file="JsonElementExtensionsTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Helpers;

namespace AIUsageTracker.Tests.Core.Helpers;

public class JsonElementExtensionsTests
{
    [Fact]
    public void ReadString_TopLevelProperty_ReturnsValue()
    {
        using var doc = JsonDocument.Parse("{\"name\":\"test-value\"}");

        var result = doc.RootElement.ReadString("name");

        Assert.Equal("test-value", result);
    }

    [Fact]
    public void ReadString_NestedPath_ReturnsValue()
    {
        using var doc = JsonDocument.Parse("{\"level1\":{\"level2\":{\"target\":\"deep-value\"}}}");

        var result = doc.RootElement.ReadString("level1", "level2", "target");

        Assert.Equal("deep-value", result);
    }

    [Fact]
    public void ReadString_MissingProperty_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("{\"name\":\"test\"}");

        Assert.Null(doc.RootElement.ReadString("nonexistent"));
    }

    [Fact]
    public void ReadString_NonStringValue_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("{\"count\":42}");

        Assert.Null(doc.RootElement.ReadString("count"));
    }

    [Fact]
    public void ReadString_EmptyPath_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("{\"name\":\"test\"}");

        Assert.Null(doc.RootElement.ReadString());
    }

    [Fact]
    public void ReadDouble_NumberValue_ReturnsDouble()
    {
        using var doc = JsonDocument.Parse("{\"ratio\":0.75}");

        var result = doc.RootElement.ReadDouble("ratio");

        Assert.Equal(0.75, result);
    }

    [Fact]
    public void ReadDouble_StringEncodedNumber_ReturnsDouble()
    {
        using var doc = JsonDocument.Parse("{\"ratio\":\"0.75\"}");

        var result = doc.RootElement.ReadDouble("ratio");

        Assert.Equal(0.75, result);
    }

    [Fact]
    public void ReadDouble_IntegerValue_ReturnsDouble()
    {
        using var doc = JsonDocument.Parse("{\"count\":42}");

        var result = doc.RootElement.ReadDouble("count");

        Assert.Equal(42.0, result);
    }

    [Fact]
    public void ReadDouble_MissingProperty_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("{\"name\":\"test\"}");

        Assert.Null(doc.RootElement.ReadDouble("nonexistent"));
    }

    [Fact]
    public void ReadDouble_NonNumericString_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("{\"value\":\"not-a-number\"}");

        Assert.Null(doc.RootElement.ReadDouble("value"));
    }

    [Fact]
    public void ReadBool_TrueValue_ReturnsTrue()
    {
        using var doc = JsonDocument.Parse("{\"active\":true}");

        var result = doc.RootElement.ReadBool("active");

        Assert.True(result);
    }

    [Fact]
    public void ReadBool_FalseValue_ReturnsFalse()
    {
        using var doc = JsonDocument.Parse("{\"active\":false}");

        var result = doc.RootElement.ReadBool("active");

        Assert.False(result);
    }

    [Fact]
    public void ReadBool_MissingProperty_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("{\"name\":\"test\"}");

        Assert.Null(doc.RootElement.ReadBool("nonexistent"));
    }

    [Fact]
    public void ReadBool_NonBoolValue_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("{\"count\":1}");

        Assert.Null(doc.RootElement.ReadBool("count"));
    }

    [Fact]
    public void ReadString_DeeplyNestedMissingMiddle_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("{\"a\":{\"b\":\"value\"}}");

        Assert.Null(doc.RootElement.ReadString("a", "missing", "c"));
    }
}
