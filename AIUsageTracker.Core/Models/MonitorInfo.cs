// <copyright file="MonitorInfo.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models
{
    public class MonitorInfo
    {
        public int Port { get; set; }

        public string? StartedAt { get; set; }

        public int ProcessId { get; set; }

        public bool DebugMode { get; set; }

        public IReadOnlyList<string>? Errors { get; set; }

        public string? MachineName { get; set; }

        public string? UserName { get; set; }
    }
}
