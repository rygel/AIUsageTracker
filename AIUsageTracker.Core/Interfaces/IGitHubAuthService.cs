// <copyright file="IGitHubAuthService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Interfaces;

public interface IGitHubAuthService
{
    /// <summary>
    /// Gets a value indicating whether checks if the user is currently authenticated.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Initiates the Device Flow. Returns the user code, device code, and verification URI.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task<(string DeviceCode, string UserCode, string VerificationUri, int ExpiresIn, int Interval)> InitiateDeviceFlowAsync();

    /// <summary>
    /// Polls GitHub for the access token using the device code.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task<string?> PollForTokenAsync(string deviceCode, int interval);

    /// <summary>
    /// Refreshes the access token if needed (though Device Flow tokens generally don't expire quickly, keeping for completeness).
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task<string?> RefreshTokenAsync(string refreshToken);

    /// <summary>
    /// Gets the currently authenticated token, if any.
    /// </summary>
    /// <returns></returns>
    string? GetCurrentToken();

    /// <summary>
    /// Logs out by clearing the stored token.
    /// </summary>
    void Logout();

    /// <summary>
    /// Initializes the service with a previously stored token.
    /// </summary>
    void InitializeToken(string token);

    /// <summary>
    /// Gets the username of the authenticated user.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task<string?> GetUsernameAsync();
}
