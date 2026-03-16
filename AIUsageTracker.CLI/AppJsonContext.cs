// <copyright file="AppJsonContext.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.CLI;

[JsonSerializable(typeof(List<ProviderUsage>))]
[JsonSerializable(typeof(List<ProviderConfig>))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
