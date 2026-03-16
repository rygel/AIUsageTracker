// <copyright file="AuthDiagnosticsSnapshot.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Services;

internal sealed record AuthDiagnosticsSnapshot(
    string ProviderId,
    bool Configured,
    string AuthSource,
    string FallbackPathUsed,
    string TokenAgeBucket,
    bool HasUserIdentity);
