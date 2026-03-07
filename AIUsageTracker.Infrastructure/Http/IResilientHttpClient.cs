using System.Net;

namespace AIUsageTracker.Infrastructure.Http;

public interface IResilientHttpClient
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);
}
