// <copyright file="UpdateCheckerFactory.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net.Http;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.Services;

public sealed class UpdateCheckerFactory : IUpdateCheckerFactory
{
    private readonly ILogger<GitHubUpdateChecker> _logger;
    private readonly HttpClient _httpClient;

    public UpdateCheckerFactory(ILogger<GitHubUpdateChecker> logger, HttpClient httpClient)
    {
        this._logger = logger;
        this._httpClient = httpClient;
    }

    public IUpdateCheckerService Create(UpdateChannel channel)
    {
        return new GitHubUpdateChecker(this._logger, this._httpClient, channel);
    }
}
