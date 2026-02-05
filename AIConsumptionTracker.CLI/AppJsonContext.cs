using System.Text.Json.Serialization;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.CLI;

[JsonSerializable(typeof(List<ProviderUsage>))]
[JsonSerializable(typeof(List<ProviderConfig>))]
internal partial class AppJsonContext : JsonSerializerContext
{
}