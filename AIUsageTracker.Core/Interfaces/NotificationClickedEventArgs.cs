// <copyright file="NotificationClickedEventArgs.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Interfaces
{
    public class NotificationClickedEventArgs : EventArgs
    {
        public string Action { get; set; } = string.Empty;

        public string Data { get; set; } = string.Empty;
    }
}
