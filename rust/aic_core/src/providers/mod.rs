// Provider modules - one file per provider for maintainability

pub mod anthropic;
pub mod antigravity;
pub mod codex;
pub mod deepseek;
pub mod gemini;
pub mod generic_payg;
pub mod github_copilot;
pub mod kimi;
pub mod mistral;
pub mod minimax;
pub mod minimax_io;
pub mod openai;
pub mod opencode;
pub mod opencode_zen;
pub mod openrouter;
pub mod simulated;
pub mod synthetic;
pub mod zai;

// Re-export all providers
pub use anthropic::AnthropicProvider;
pub use antigravity::AntigravityProvider;
pub use codex::CodexProvider;
pub use deepseek::DeepSeekProvider;
pub use gemini::GeminiProvider;
pub use generic_payg::GenericPayAsYouGoProvider;
pub use github_copilot::GitHubCopilotProvider;
pub use kimi::KimiProvider;
pub use mistral::MistralProvider;
pub use minimax::MinimaxProvider;
pub use minimax_io::MinimaxIOProvider;
pub use openai::OpenAIProvider;
pub use opencode::OpenCodeProvider;
pub use opencode_zen::OpenCodeZenProvider;
pub use openrouter::OpenRouterProvider;
pub use simulated::SimulatedProvider;
pub use synthetic::SyntheticProvider;
pub use zai::ZaiProvider;
