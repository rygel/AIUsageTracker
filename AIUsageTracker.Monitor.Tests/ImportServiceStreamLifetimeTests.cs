// <copyright file="ImportServiceStreamLifetimeTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text;
using AIUsageTracker.Monitor.Services;
using Moq;

namespace AIUsageTracker.Monitor.Tests;

public class ImportServiceStreamLifetimeTests
{
    // Regression for CA2024: StreamReader must be constructed with leaveOpen:true so
    // ImportService does not dispose a stream it does not own. After ImportHistoryAsync
    // returns, the caller's stream must still be readable (the caller owns its lifetime).
    [Fact]
    public async Task ImportHistoryAsync_LeavesCallerStreamOpenAfterReturnAsync()
    {
        var csv = "provider_id,ProviderName,is_available,requests_used\nopenai,OpenAI,1,50\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var service = new ImportService(Mock.Of<IUsageDatabase>());

        await service.ImportHistoryAsync(stream, "csv");

        // The caller owns the stream. ImportService must leave it open and usable.
        Assert.True(stream.CanRead, "ImportService disposed the caller-owned stream (CA2024 regression).");

        await stream.DisposeAsync();
    }

    [Fact]
    public async Task ImportHistoryAsync_LeavesCallerStreamOpenOnFailureTooAsync()
    {
        // Malformed CSV triggers the error path; the stream must still survive.
        var csv = "not_enough_columns\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var service = new ImportService(Mock.Of<IUsageDatabase>());

        await service.ImportHistoryAsync(stream, "csv");

        Assert.True(stream.CanRead, "ImportService disposed the caller-owned stream on the error path.");
        await stream.DisposeAsync();
    }
}
