// <copyright file="ISingleInstanceLockService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim.Services;

public interface ISingleInstanceLockService
{
    bool TryAcquire();

    void Release();
}
