// <copyright file="IUpdateCheckerFactory.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim.Services;

public interface IUpdateCheckerFactory
{
    IUpdateCheckerService Create(UpdateChannel channel);
}
