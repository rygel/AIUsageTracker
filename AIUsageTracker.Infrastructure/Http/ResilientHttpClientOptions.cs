using System.Net;

namespace AIUsageTracker.Infrastructure.Http;

public class ResilientHttpClientOptions
{
    public int MaxRetryCount { get; set; } = 3;
    public double BackoffBase { get; set; } = 2;
    public TimeSpan CircuitBreakerDuration { get; set; } = TimeSpan.FromSeconds(30);
    public int CircuitBreakerFailureThreshold { get; set; } = 5;
    
    public IReadOnlyList<HttpStatusCode> RetryStatusCodes { get; set; } = new List<HttpStatusCode>
    {
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    };
    
    public IReadOnlyList<HttpStatusCode> CircuitBreakerStatusCodes { get; set; } = new List<HttpStatusCode>
    {
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    };
}
