# Architecture Improvement Tasks

**Generated: 2026-03-04**

## Major Duplications Found

### 1. HTTP Client Management (15 occurrences)
- **Pattern**: Each provider declares `private readonly HttpClient _httpClient`
- **Impact**: No centralized retry/resilience policies, inconsistent timeout handling
- **Affected**: All 15 providers except Antigravity

### 2. API Endpoint Patterns (28 occurrences)
- **Pattern**: Hardcoded API URLs in string literals
- **Impact**: No version management, difficult to update endpoints
- **Examples**:
  - OpenAI: `https://api.openai.com/v1/models`
  - OpenRouter: `https://openrouter.ai/api/v1/credits`
  - GitHub: `https://api.github.com/user`
  - ZAI: `https://api.z.ai/api/monitor/usage/quota/limit`

### 3. Generic Exception Handling (29 occurrences)
- **Pattern**: `catch (Exception ex)` without specific handling
- **Impact**: Swallows all exceptions, no retry logic
- **Location**: All providers

### 4. Request Creation Patterns (15 occurrences)
- **Pattern**: `new HttpRequestMessage(HttpMethod.Get, url)` repeated
- **Impact**: No centralized request building

## Recommendations

### High Priority (P1)

1. **Centralize API Endpoints**: Create `IProviderEndpoints` interface or config class
   - Define standard endpoint structure
   - Allow version management
   - Enable endpoint override via config

2. **Consolidate JSON Deserialization**: Already using `ReadFromJsonAsync` but not consistently
   - Standardize on `ReadFromJsonAsync<T>` across all providers
   - Create typed response models for all APIs

3. **Add Specific Exception Types**: Replace generic `catch (Exception)` with specific exception types
   - Create provider-specific exception types
   - Add retry logic for transient failures
   - Log specific exception details

### Medium Priority (P2)

1. **Standardize Request Builders**: Create extension methods for common request patterns
   - `CreateGetRequest(string url)`
   - `CreatePostRequest(string url, object body)`
   - `AddBearerToken(string token)`
   - Reduce request creation boilerplate

2. **Shared Helper Methods**: Move common parsing logic to utility classes
   - Unix timestamp conversion helpers
   - Percentage calculation helpers
   - Reset time parsing helpers

### Low Priority (P3)

1. **Magic String Literals**: Extract to constants where appropriate
   - API endpoint URLs
   - HTTP headers
   - Error messages

2. **Configuration-Driven URLs**: Allow endpoint override via config
   - Support custom API endpoints
   - Proxy support for corporate environments

## Implementation Order

1. Phase 1: High Priority API Endpoints (P1)
2. Phase 2: Exception Handling Improvements (P1)
3. Phase 3: Request Builder Extensions (P2)
4. Phase 4: Shared Utilities (P2)
5. Phase 5: Configuration Support (P3)

## Estimated Effort

- P1 Tasks: 2-3 days
- P2 Tasks: 1-2 days
- P3 Tasks: 1 day

Total: 4-6 days for full implementation

## Notes

- ResilientHttpClient already provides retry logic - need to integrate better
- ProviderBase provides some error handling - could be extended
- DateTimeExtensions already exists - leverage for timestamp helpers
- ProviderLoggingExtensions already created - use consistently
