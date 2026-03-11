// <copyright file="ICodexAuthService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Interfaces;

public interface ICodexAuthService
{
    string? GetAccessToken();

    string? GetAccountId();
}
