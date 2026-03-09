// <copyright file="MonitorInfoTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Core.Models
{
    using System.Text.Json;
    using AIUsageTracker.Core.Models;
    using Xunit;

    public class MonitorInfoTests
    {
        [Fact]
        public void MonitorInfo_DefaultValues_AreCorrect()
        {
            var info = new MonitorInfo();

            Assert.Equal(0, info.Port);
            Assert.Equal(0, info.ProcessId);
            Assert.Null(info.StartedAt);
            Assert.Null(info.Errors);
            Assert.Null(info.MachineName);
            Assert.Null(info.UserName);
        }

        [Fact]
        public void MonitorInfo_CanBePopulated()
        {
            var info = new MonitorInfo
            {
                Port = 5000,
                ProcessId = 12345,
                StartedAt = "2026-03-01 12:00:00",
                DebugMode = true,
                MachineName = "TESTMACHINE",
                UserName = "testuser",
                Errors = new List<string> { "Test error" }
            };

            Assert.Equal(5000, info.Port);
            Assert.Equal(12345, info.ProcessId);
            Assert.Equal("2026-03-01 12:00:00", info.StartedAt);
            Assert.True(info.DebugMode);
            Assert.Equal("TESTMACHINE", info.MachineName);
            Assert.Equal("testuser", info.UserName);
            Assert.Single(info.Errors);
        }

        [Fact]
        public void MonitorInfo_CanBeSerializedAndDeserialized()
        {
            var original = new MonitorInfo
            {
                Port = 5001,
                ProcessId = 99999,
                StartedAt = "2026-02-27 12:00:00",
                DebugMode = true,
                MachineName = "TEST-PC",
                UserName = "developer",
                Errors = new List<string> { "Error 1", "Error 2" }
            };

            var json = JsonSerializer.Serialize(original, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var deserialized = JsonSerializer.Deserialize<MonitorInfo>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            Assert.NotNull(deserialized);
            Assert.Equal(original.Port, deserialized.Port);
            Assert.Equal(original.ProcessId, deserialized.ProcessId);
            Assert.Equal(original.DebugMode, deserialized.DebugMode);
            Assert.Equal(original.Errors?.Count, deserialized.Errors?.Count);
        }
    }
}
