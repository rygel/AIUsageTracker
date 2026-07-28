// <copyright file="ProviderEndpoints.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Infrastructure.Constants;

public static class ProviderEndpoints
{
    public static class OpenAI
    {
        public const string ProfileClaimKey = "https://api.openai.com/profile";
    }

    public static class Minimax
    {
        public const string TokenPlanRemains = "https://api.minimax.io/v1/token_plan/remains";
        public const string CodingPlanRemains = "https://api.minimax.io/v1/api/openplatform/coding_plan/remains";
    }
}
