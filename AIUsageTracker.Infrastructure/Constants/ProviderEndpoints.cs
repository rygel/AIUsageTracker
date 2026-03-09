// <copyright file="ProviderEndpoints.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Infrastructure.Constants
{
    /// <summary>
    /// API endpoint URLs for all providers.
    /// These can be overridden via configuration in the future.
    /// </summary>
    public static class ProviderEndpoints
    {
        /// <summary>
        /// OpenAI API endpoints
        /// </summary>
        public static class OpenAI
        {
            public const string BaseUrl = "https://api.openai.com";
            public const string Models = "https://api.openai.com/v1/models";
            public const string WhamUsage = "https://chatgpt.com/backend-api/wham/usage";

            // JWT claim keys
            public const string ProfileClaimKey = "https://api.openai.com/profile";
            public const string AuthClaimKey = "https://api.openai.com/auth";
        }

        /// <summary>
        /// Anthropic API endpoints
        /// </summary>
        public static class Anthropic
        {
            public const string BaseUrl = "https://api.anthropic.com";
            public const string Messages = "https://api.anthropic.com/v1/messages";
        }

        /// <summary>
        /// Google/Gemini API endpoints
        /// </summary>
        public static class Gemini
        {
            public const string OAuthTokenUrl = "https://oauth2.googleapis.com/token";
            public const string QuotaUrl = "https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota";
        }

        /// <summary>
        /// GitHub API endpoints
        /// </summary>
        public static class GitHub
        {
            public const string BaseUrl = "https://api.github.com";
            public const string User = "https://api.github.com/user";
            public const string CopilotUser = "https://api.github.com/copilot_internal/user";
            public const string CopilotToken = "https://api.github.com/copilot_internal/v2/token";
        }

        /// <summary>
        /// Z.AI API endpoints
        /// </summary>
        public static class ZAI
        {
            public const string BaseUrl = "https://api.z.ai";
            public const string QuotaLimit = "https://api.z.ai/api/monitor/usage/quota/limit";
        }

        /// <summary>
        /// OpenRouter API endpoints
        /// </summary>
        public static class OpenRouter
        {
            public const string BaseUrl = "https://openrouter.ai";
            public const string Credits = "https://openrouter.ai/api/v1/credits";
            public const string Key = "https://openrouter.ai/api/v1/key";
        }

        /// <summary>
        /// Mistral AI API endpoints
        /// </summary>
        public static class Mistral
        {
            public const string BaseUrl = "https://api.mistral.ai";
            public const string Models = "https://api.mistral.ai/v1/models";
        }

        /// <summary>
        /// DeepSeek API endpoints
        /// </summary>
        public static class DeepSeek
        {
            public const string BaseUrl = "https://api.deepseek.com";
            public const string UserBalance = "https://api.deepseek.com/user/balance";
        }

        /// <summary>
        /// Kimi API endpoints
        /// </summary>
        public static class Kimi
        {
            public const string BaseUrl = "https://api.kimi.com";
            public const string CodingUsages = "https://api.kimi.com/coding/v1/usages";
        }

        /// <summary>
        /// Minimax API endpoints
        /// </summary>
        public static class Minimax
        {
            public const string BaseUrl = "https://api.minimax.io";
            public const string UserUsage = "https://api.minimax.io/v1/user/usage";
            public const string ChatUserUsage = "https://api.minimax.chat/v1/user/usage";
        }

        /// <summary>
        /// Xiaomi AI API endpoints
        /// </summary>
        public static class Xiaomi
        {
            public const string BaseUrl = "https://api.xiaomimimo.com";
            public const string UserBalance = "https://api.xiaomimimo.com/v1/user/balance";
        }

        /// <summary>
        /// OpenCode API endpoints
        /// </summary>
        public static class OpenCode
        {
            public const string BaseUrl = "https://api.opencode.ai";
            public const string Credits = "https://api.opencode.ai/v1/credits";
        }

        /// <summary>
        /// Synthetic API endpoints
        /// </summary>
        public static class Synthetic
        {
            public const string BaseUrl = "https://api.synthetic.new";
            public const string Quotas = "https://api.synthetic.new/v2/quotas";
        }
    }
}
