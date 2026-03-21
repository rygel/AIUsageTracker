// <copyright file="WpfProviderIconServiceFactory.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.Services;

internal sealed class WpfProviderIconServiceFactory : IWpfProviderIconServiceFactory
{
    private readonly ILogger<WpfProviderIconService> _logger;

    public WpfProviderIconServiceFactory(ILogger<WpfProviderIconService> logger)
    {
        this._logger = logger;
    }

    public Func<string, FrameworkElement> Create(Func<string, SolidColorBrush, SolidColorBrush> resolveResourceBrush)
    {
        var service = new WpfProviderIconService(this._logger, resolveResourceBrush);
        return service.CreateIcon;
    }
}
