// <copyright file="UsageAlertsServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AIUsageTracker.Monitor.Tests;

public class UsageAlertsServiceTests
{
    private readonly Mock<IUsageDatabase> _mockDatabase;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<IAppPathProvider> _mockPathProvider;
    private readonly Mock<ConfigService> _mockConfigService;
    private readonly UsageAlertsService _service;

    public UsageAlertsServiceTests()
    {
        this._mockDatabase = new Mock<IUsageDatabase>();
        this._mockNotificationService = new Mock<INotificationService>();
        this._mockPathProvider = new Mock<IAppPathProvider>();

        var configLogger = new Mock<ILogger<ConfigService>>();
        this._mockConfigService = new Mock<ConfigService>(configLogger.Object, this._mockPathProvider.Object);

        var alertsLogger = new Mock<ILogger<UsageAlertsService>>();
        this._service = new UsageAlertsService(
            alertsLogger.Object,
            this._mockDatabase.Object,
            this._mockNotificationService.Object,
            this._mockConfigService.Object);
    }

    [Fact]
    public void CheckUsageAlertsAsync_UsageAboveThreshold_TriggersNotification()
    {
        var prefs = new AppPreferences { EnableNotifications = true, NotificationThreshold = 90.0 };
        var configs = new List<ProviderConfig>
        {
            new ProviderConfig { ProviderId = "test", EnableNotifications = true },
        };
        var usages = new List<ProviderUsage>
        {
            new ProviderUsage
            {
                ProviderId = "test",
                ProviderName = "Test Provider",
                UsedPercent = 95.0,
                IsAvailable = true,
            },
        };

        this._service.CheckUsageAlerts(usages, prefs, configs);

        this._mockNotificationService.Verify(n => n.ShowUsageAlert("Test Provider", 95.0), Times.Once);
    }

    [Fact]
    public void CheckUsageAlertsAsync_QuotaRemainingLow_TriggersNotificationFromUsedPercent()
    {
        var prefs = new AppPreferences { EnableNotifications = true, NotificationThreshold = 90.0 };
        var configs = new List<ProviderConfig>
        {
            new ProviderConfig { ProviderId = "test", EnableNotifications = true },
        };
        var usages = new List<ProviderUsage>
        {
            new ProviderUsage
            {
                ProviderId = "test",
                ProviderName = "Test Provider",
                UsedPercent = 95.0,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                IsAvailable = true,
            },
        };

        this._service.CheckUsageAlerts(usages, prefs, configs);

        this._mockNotificationService.Verify(n => n.ShowUsageAlert("Test Provider", 95.0), Times.Once);
    }

    [Fact]
    public void CheckUsageAlertsAsync_QuotaRemainingHigh_DoesNotTriggerNotification()
    {
        var prefs = new AppPreferences { EnableNotifications = true, NotificationThreshold = 90.0 };
        var configs = new List<ProviderConfig>
        {
            new ProviderConfig { ProviderId = "test", EnableNotifications = true },
        };
        var usages = new List<ProviderUsage>
        {
            new ProviderUsage
            {
                ProviderId = "test",
                ProviderName = "Test Provider",
                UsedPercent = 30.0,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                IsAvailable = true,
            },
        };

        this._service.CheckUsageAlerts(usages, prefs, configs);

        this._mockNotificationService.Verify(n => n.ShowUsageAlert(It.IsAny<string>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public void CheckUsageAlertsAsync_NotificationsDisabledGlobally_DoesNotTrigger()
    {
        var prefs = new AppPreferences { EnableNotifications = false, NotificationThreshold = 90.0 };
        var configs = new List<ProviderConfig>
        {
            new ProviderConfig { ProviderId = "test", EnableNotifications = true },
        };
        var usages = new List<ProviderUsage>
        {
            new ProviderUsage
            {
                ProviderId = "test",
                ProviderName = "Test Provider",
                UsedPercent = 95.0,
                IsAvailable = true,
            },
        };

        this._service.CheckUsageAlerts(usages, prefs, configs);

        this._mockNotificationService.Verify(n => n.ShowUsageAlert(It.IsAny<string>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public void CheckUsageAlertsAsync_ProviderNotificationsDisabled_DoesNotTrigger()
    {
        var prefs = new AppPreferences { EnableNotifications = true, NotificationThreshold = 90.0 };
        var configs = new List<ProviderConfig>
        {
            new ProviderConfig { ProviderId = "test", EnableNotifications = false },
        };
        var usages = new List<ProviderUsage>
        {
            new ProviderUsage
            {
                ProviderId = "test",
                ProviderName = "Test Provider",
                UsedPercent = 95.0,
                IsAvailable = true,
            },
        };

        this._service.CheckUsageAlerts(usages, prefs, configs);

        this._mockNotificationService.Verify(n => n.ShowUsageAlert(It.IsAny<string>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public void CheckUsageAlertsAsync_UsageThresholdNotificationsDisabled_DoesNotTrigger()
    {
        var prefs = new AppPreferences
        {
            EnableNotifications = true,
            NotifyOnUsageThreshold = false,
            NotificationThreshold = 90.0,
        };
        var configs = new List<ProviderConfig>
        {
            new ProviderConfig { ProviderId = "test", EnableNotifications = true },
        };
        var usages = new List<ProviderUsage>
        {
            new ProviderUsage
            {
                ProviderId = "test",
                ProviderName = "Test Provider",
                UsedPercent = 95.0,
                IsAvailable = true,
            },
        };

        this._service.CheckUsageAlerts(usages, prefs, configs);

        this._mockNotificationService.Verify(n => n.ShowUsageAlert(It.IsAny<string>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public void CheckUsageAlerts_ExpiredSubscription_TriggersExpiredNotification()
    {
        var prefs = new AppPreferences { EnableNotifications = true, NotifyOnSubscriptionExpired = true };
        var configs = new List<ProviderConfig>
        {
            new ProviderConfig { ProviderId = "synthetic", EnableNotifications = true },
        };
        var usages = new List<ProviderUsage>
        {
            new ProviderUsage
            {
                ProviderId = "synthetic",
                ProviderName = "Synthetic.new",
                IsAvailable = false,
                State = ProviderUsageState.Expired,
            },
        };

        this._service.CheckUsageAlerts(usages, prefs, configs);

        this._mockNotificationService.Verify(n => n.ShowSubscriptionExpired("Synthetic.new"), Times.Once);
        this._mockNotificationService.Verify(n => n.ShowUsageAlert(It.IsAny<string>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public void CheckUsageAlerts_ExpiredSubscription_DoesNotTrigger_WhenPreferenceDisabled()
    {
        var prefs = new AppPreferences { EnableNotifications = true, NotifyOnSubscriptionExpired = false };
        var configs = new List<ProviderConfig>
        {
            new ProviderConfig { ProviderId = "synthetic", EnableNotifications = true },
        };
        var usages = new List<ProviderUsage>
        {
            new ProviderUsage
            {
                ProviderId = "synthetic",
                ProviderName = "Synthetic.new",
                IsAvailable = false,
                State = ProviderUsageState.Expired,
            },
        };

        this._service.CheckUsageAlerts(usages, prefs, configs);

        this._mockNotificationService.Verify(n => n.ShowSubscriptionExpired(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void CheckUsageAlertsAsync_QuietHoursAlwaysEnabled_DoesNotTrigger()
    {
        var prefs = new AppPreferences
        {
            EnableNotifications = true,
            NotifyOnUsageThreshold = true,
            NotificationThreshold = 90.0,
            EnableQuietHours = true,
            QuietHoursStart = "22:00",
            QuietHoursEnd = "22:00",
        };
        var configs = new List<ProviderConfig>
        {
            new ProviderConfig { ProviderId = "test", EnableNotifications = true },
        };
        var usages = new List<ProviderUsage>
        {
            new ProviderUsage
            {
                ProviderId = "test",
                ProviderName = "Test Provider",
                UsedPercent = 95.0,
                IsAvailable = true,
            },
        };

        this._service.CheckUsageAlerts(usages, prefs, configs);

        this._mockNotificationService.Verify(n => n.ShowUsageAlert(It.IsAny<string>(), It.IsAny<double>()), Times.Never);
    }
}
