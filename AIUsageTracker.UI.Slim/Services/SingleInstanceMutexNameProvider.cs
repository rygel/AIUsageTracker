// <copyright file="SingleInstanceMutexNameProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Runtime;

namespace AIUsageTracker.UI.Slim.Services;

internal sealed class SingleInstanceMutexNameProvider : ISingleInstanceMutexNameProvider
{
    public string GetMutexName()
    {
        return MutexNameBuilder.BuildLocalName("AIUsageTracker_SlimUI_");
    }
}
