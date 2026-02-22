using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.CLI;

[JsonSerializable(typeof(List<ProviderUsage>))]
[JsonSerializable(typeof(List<ProviderConfig>))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
