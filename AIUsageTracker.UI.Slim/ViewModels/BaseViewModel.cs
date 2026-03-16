// <copyright file="BaseViewModel.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using CommunityToolkit.Mvvm.ComponentModel;

namespace AIUsageTracker.UI.Slim.ViewModels;

/// <summary>
/// Base class for all ViewModels in the application.
/// Inherits from CommunityToolkit.Mvvm's ObservableObject for INotifyPropertyChanged support.
/// </summary>
public abstract class BaseViewModel : ObservableObject
{
}
