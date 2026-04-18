// <copyright file="WpfProviderIconServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIUsageTracker.UI.Slim.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.UI;

public sealed class WpfProviderIconServiceTests
{
    // Brush resolver that always returns the fallback — no WPF resource dictionary needed.
    private static SolidColorBrush PassthroughBrush(string providerId, SolidColorBrush fallback) => fallback;

    private static WpfProviderIconService CreateService() =>
        new(NullLogger.Instance, PassthroughBrush);

    // ── CreateIcon — fallback badge ───────────────────────────────────────────
    [Fact]
    public void CreateIcon_ReturnsFrameworkElement_ForUnknownProvider()
    {
        FrameworkElement? result = null;
        Exception? ex = null;

        var thread = new Thread(() =>
        {
            try
            {
                var service = CreateService();
                result = service.CreateIcon("unknown-provider-xyz");
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            throw new Exception("STA thread threw", ex);
        }

        Assert.NotNull(result);
    }

    [Fact]
    public void CreateIcon_FallbackBadge_IsGrid_With16x16Size()
    {
        double width = -1, height = -1;
        Exception? ex = null;

        var thread = new Thread(() =>
        {
            try
            {
                var service = CreateService();
                var element = service.CreateIcon("unknown-provider-xyz");
                if (element is Grid g)
                {
                    width = g.Width;
                    height = g.Height;
                }
                else
                {
                    width = element.Width;
                    height = element.Height;
                }
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            throw new Exception("STA thread threw", ex);
        }

        Assert.Equal(16, width);
        Assert.Equal(16, height);
    }

    [Fact]
    public void CreateIcon_FallbackBadge_HasTwoChildren_CircleAndText()
    {
        int? childCount = null;
        bool? hasBorder = null;
        bool? hasText = null;
        Exception? ex = null;

        var thread = new Thread(() =>
        {
            try
            {
                var service = CreateService();
                var element = service.CreateIcon("unknown-provider-xyz");
                if (element is Grid g)
                {
                    childCount = g.Children.Count;
                    hasBorder = g.Children.OfType<Border>().Any();
                    hasText = g.Children.OfType<TextBlock>().Any();
                }
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            throw new Exception("STA thread threw", ex);
        }

        Assert.Equal(2, childCount);
        Assert.True(hasBorder, "Expected a Border child (the circle)");
        Assert.True(hasText, "Expected a TextBlock child (the initial)");
    }

    [Fact]
    public void CreateIcon_ForKnownProvider_ReturnsNonNullElement()
    {
        // Uses a known provider ID that is resolvable but won't have an
        // SVG on disk in the test environment — fallback badge path still exercised.
        FrameworkElement? result = null;
        Exception? ex = null;

        var thread = new Thread(() =>
        {
            try
            {
                var service = CreateService();
                result = service.CreateIcon("openai");
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            throw new Exception("STA thread threw", ex);
        }

        Assert.NotNull(result);
    }

    [Fact]
    public void CreateIcon_CalledTwiceWithSameProvider_ReturnsDifferentInstancesOfFallback()
    {
        // Fallback badges (no SVG) are NOT cached — each call creates a fresh Grid.
        // This verifies the badge path doesn't accidentally share mutable WPF element instances.
        FrameworkElement? first = null;
        FrameworkElement? second = null;
        Exception? ex = null;

        var thread = new Thread(() =>
        {
            try
            {
                var service = CreateService();
                first = service.CreateIcon("unknown-provider-xyz");
                second = service.CreateIcon("unknown-provider-xyz");
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            throw new Exception("STA thread threw", ex);
        }

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
    }
}
