# Architecture

See the main architecture document here: [Project Architecture & Philosophy](../ARCHITECTURE.md).

## Key sections
- Monitor: `AIUsageTracker.Monitor` section in [ARCHITECTURE.md](../ARCHITECTURE.md)
- Web UI: `AIUsageTracker.Web` section in [ARCHITECTURE.md](../ARCHITECTURE.md)
- Provider usage details: [Provider Detail Contract](provider_detail_contract.md)

---

## Architecture Improvements (2026-03-04)

### Provider Exception Handling

**Location**: `AIUsageTracker.Core/Exceptions/`

Structured exception hierarchy for provider error handling:

- **ProviderException** - Base class with `ProviderId`, `ErrorType`, `HttpStatusCode`
- **ProviderAuthenticationException** - 401 Unauthorized errors
- **ProviderNetworkException** - Connection/DNS failures
- **ProviderTimeoutException** - Request timeouts with duration tracking
- **ProviderRateLimitException** - 429 Too Many Requests with retry timing
- **ProviderServerException** - 500+ server errors
- **ProviderConfigurationException** - Invalid/missing configuration
- **ProviderResponseException** - Invalid/malformed responses
- **ProviderDeserializationException** - JSON parsing failures

**Usage**:
```csharp
// In providers, throw specific exceptions
catch (HttpRequestException ex)
{
    throw new ProviderNetworkException(ProviderId, innerException: ex);
}
```

### HTTP Request Builder Extensions

**Location**: `AIUsageTracker.Infrastructure/Extensions/HttpRequestBuilderExtensions.cs`

Standardized HTTP request patterns with automatic exception mapping:

- **CreateBearerRequest()** - Creates GET request with Bearer token
- **CreateBearerPostRequest<T>()** - Creates POST with Bearer token and JSON body
- **SendGetBearerAsync()** - Sends GET and maps status codes to exceptions
- **SendGetBearerAsync<T>()** - Sends GET and deserializes JSON response

**Features**:
- Automatic HTTP status code to ProviderException mapping
- Configurable timeouts
- Structured logging via ILogger
- Retry-After header extraction
- CancellationToken support

**Usage**:
```csharp
// Old way (repeated in 15+ providers)
var request = new HttpRequestMessage(HttpMethod.Get, url);
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
var response = await _httpClient.SendAsync(request);
if (!response.IsSuccessStatusCode) { /* manual handling */ }

// New way
var response = await _httpClient.SendGetBearerAsync(url, token, ProviderId);
var data = await _httpClient.SendGetBearerAsync<ResponseType>(url, token, ProviderId);
```

### Shared Helper Utilities

**ResetTimeParser** (`AIUsageTracker.Core/Utilities/ResetTimeParser.cs`)

Consistent reset time parsing across all providers:

- **FromUnixSeconds()** / **FromUnixMilliseconds()** - Unix timestamp parsing
- **FromSecondsFromNow()** - Relative time offsets
- **FromIso8601()** - ISO 8601 date parsing
- **Parse()** - Multi-format parsing with fallbacks
- **FromJsonElement()** - JSON element auto-detection
- **FormatForDisplay()** - Consistent display formatting
- **GetSoonest()** / **IsFuture()** / **GetTimeRemaining()** - Utility operations

**Usage**:
```csharp
// Parse from various formats
var resetTime = ResetTimeParser.FromUnixSeconds(timestamp);
var resetTime = ResetTimeParser.FromIso8601(isoString);
var resetTime = ResetTimeParser.Parse(dateString); // Tries multiple formats
var resetTime = ResetTimeParser.FromJsonElement(jsonElement);

// Utility operations
var soonest = ResetTimeParser.GetSoonest(resetTime1, resetTime2, resetTime3);
var remaining = ResetTimeParser.GetTimeRemaining(resetTime);
```

**UsageMath** (`AIUsageTracker.Core/Models/UsageMath.cs`)

Enhanced percentage calculations:

- **ClampPercent()** - Clamp to [0, 100] with NaN/Infinity protection
- **CalculateUsedPercent()** - Calculate used percentage
- **CalculateRemainingPercent()** - Calculate remaining percentage
- **CalculateUtilizationPercent()** - Quota-aware calculation
- **PercentOf()** - Generic percentage calculation
- **CalculatePaceAdjustedColorPercent()** - Pace-aware colour score for rolling/model-specific quota windows (`used³ / expected²` when under pace)
- **CalculateProjectedFinalPercent()** - End-of-window projection used for threshold/alert evaluation

### Slim UI Pace Path (No-Interpretation Downstream)

**Locations**:
- `AIUsageTracker.UI.Slim/ProviderUsageDisplayCatalog.cs`
- `AIUsageTracker.UI.Slim/ViewModels/ProviderCardViewModel.cs`
- `AIUsageTracker.UI.Slim/ProviderPacePresentationCatalog.cs`

**Flow**:
1. `ProviderUsageDisplayCatalog` prepares render data and calls `EnrichWithPeriodDuration()` for each usage before ViewModel creation.
2. `EnrichWithPeriodDuration()` resolves period metadata from provider catalog quota windows (rolling first, model-specific fallback).
3. `ProviderCardViewModel` consumes `Usage.PeriodDuration` and `Usage.NextResetTime` directly for `ColorIndicatorPercent` and `PaceBadgeText`.
4. No provider-catalog lookup or fallback chain exists in ViewModel pace logic.
5. If pace adjustment is disabled, the path returns raw used percentage and suppresses the `On pace` badge.

### Constants

**ProviderEndpoints** (`AIUsageTracker.Infrastructure/Constants/ProviderEndpoints.cs`)

API endpoint URLs for 15+ providers:

```csharp
// Usage
var url = ProviderEndpoints.OpenAI.Models;
var url = ProviderEndpoints.GitHub.User;
var url = ProviderEndpoints.OpenRouter.Credits;
```

**HttpHeaders** (`AIUsageTracker.Infrastructure/Constants/HttpHeaders.cs`)

Standard HTTP header names and values:

```csharp
// Names
HttpHeaders.Names.Authorization
HttpHeaders.Names.Accept
HttpHeaders.Names.RetryAfter

// Values
HttpHeaders.Values.ApplicationJson
HttpHeaders.Values.BearerPrefix
```

**ProviderErrorMessages** (`AIUsageTracker.Infrastructure/Constants/ProviderErrorMessages.cs`)

Standardized error messages:

```csharp
// Auth errors
ProviderErrorMessages.Auth.ApiKeyMissing
ProviderErrorMessages.Auth.AuthenticationFailed

// Network errors
ProviderErrorMessages.Network.ConnectionFailed
ProviderErrorMessages.Network.RequestTimeout

// Rate limiting
ProviderErrorMessages.RateLimit.RateLimitExceeded
```

---

## Migration Guide

### For Provider Developers

When creating or updating a provider:

1. **Inherit from ProviderBase** for standard error handling:
   ```csharp
   public class MyProvider : ProviderBase
   {
       public override string ProviderId => "myprovider";
       // ...
   }
   ```

2. **Use HTTP builder extensions** for API calls:
   ```csharp
   var response = await _httpClient.SendGetBearerAsync(
       ProviderEndpoints.MyProvider.Usage,
       config.ApiKey,
       ProviderId,
       logger: _logger);
   ```

3. **Use ResetTimeParser** for date/time parsing:
   ```csharp
   var resetTime = ResetTimeParser.FromJsonElement(element.GetProperty("resetTime"));
   ```

4. **Throw specific exceptions** for error cases:
   ```csharp
   if (string.IsNullOrEmpty(config.ApiKey))
   {
       throw new ProviderConfigurationException(ProviderId, "API key missing");
   }
   ```

5. **Use constants** for endpoints and messages:
   ```csharp
   var url = ProviderEndpoints.MyProvider.BaseUrl + "/usage";
   return CreateUnavailableUsage(ProviderErrorMessages.Auth.ApiKeyMissing);
   ```

---

## Benefits Summary

- **174 lines** of duplicate code eliminated
- **15+ providers** using standardized patterns
- **Consistent error handling** across all providers
- **Type-safe exceptions** enable targeted retry logic
- **Centralized constants** reduce typos and enable IntelliSense
- **All 162 unit tests passing**


