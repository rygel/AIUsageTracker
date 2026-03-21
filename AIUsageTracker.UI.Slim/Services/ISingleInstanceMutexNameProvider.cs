// <copyright file="ISingleInstanceMutexNameProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim.Services;

public interface ISingleInstanceMutexNameProvider
{
    string GetMutexName();
}
