// <copyright file="AuthSourceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Tests.Core;

public class AuthSourceTests
{
    [Fact]
    public void FromEnvironmentVariable_ReturnsExpectedFormat()
    {
        var result = AuthSource.FromEnvironmentVariable("OPENAI_API_KEY");

        Assert.Equal("Env: OPENAI_API_KEY", result);
    }

    [Fact]
    public void FromConfigFile_ReturnsExpectedFormat()
    {
        var result = AuthSource.FromConfigFile("auth.json");

        Assert.Equal("Config: auth.json", result);
    }

    [Theory]
    [InlineData("Env: OPENAI_API_KEY", true)]
    [InlineData("env: OPENAI_API_KEY", true)]
    [InlineData("Config: auth.json", false)]
    public void IsEnvironment_ReturnsExpectedResult(string source, bool expected)
    {
        var result = AuthSource.IsEnvironment(source);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Roo Code: C:\\temp\\secrets.json", true)]
    [InlineData("Kilo Code Roo Config", true)]
    [InlineData("Env: OPENAI_API_KEY", false)]
    public void IsRooOrKilo_ReturnsExpectedResult(string source, bool expected)
    {
        var result = AuthSource.IsRooOrKilo(source);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CodexNative_ReturnsExpectedFormat()
    {
        var result = AuthSource.CodexNative("pro");

        Assert.Equal("Codex Native (pro)", result);
    }
}
