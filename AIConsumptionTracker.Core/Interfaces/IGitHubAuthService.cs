using System.Threading.Tasks;

namespace AIConsumptionTracker.Core.Interfaces;

public interface IGitHubAuthService
{
    /// <summary>
    /// Initiates the Device Flow. Returns the user code, device code, and verification URI.
    /// </summary>
    Task<(string deviceCode, string userCode, string verificationUri, int expiresIn, int interval)> InitiateDeviceFlowAsync();

    /// <summary>
    /// Polls GitHub for the access token using the device code.
    /// </summary>
    Task<string?> PollForTokenAsync(string deviceCode, int interval);

    /// <summary>
    /// Refreshes the access token if needed (though Device Flow tokens generally don't expire quickly, keeping for completeness).
    /// </summary>
    Task<string?> RefreshTokenAsync(string refreshToken);

    /// <summary>
    /// Gets the currently authenticated token, if any.
    /// </summary>
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
    /// Checks if the user is currently authenticated.
    /// </summary>
    bool IsAuthenticated { get; }
}
