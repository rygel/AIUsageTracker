use eframe::egui;
use std::cell::RefCell;
use std::collections::HashMap;

pub struct ProviderIcons {
    icons: RefCell<HashMap<String, egui::TextureHandle>>,
}

impl ProviderIcons {
    pub fn new() -> Self {
        Self {
            icons: RefCell::new(HashMap::new()),
        }
    }

    pub fn get_or_load(&self, ctx: &egui::Context, provider_id: &str) -> Option<egui::TextureId> {
        if let Some(texture) = self.icons.borrow().get(provider_id) {
            return Some(texture.id());
        }

        if let Some(svg_content) = Self::get_embedded_svg(provider_id) {
            if let Some(image) = Self::load_svg(&svg_content, 16) {
                let texture = ctx.load_texture(
                    &format!("provider_{}", provider_id),
                    image,
                    egui::TextureOptions::default(),
                );
                let id = texture.id();
                self.icons
                    .borrow_mut()
                    .insert(provider_id.to_string(), texture);
                return Some(id);
            }
        }

        None
    }

    fn load_svg(svg_content: &[u8], size: u32) -> Option<egui::ColorImage> {
        let opt = resvg::usvg::Options::default();
        let tree = resvg::usvg::Tree::from_data(svg_content, &opt).ok()?;

        let pixmap_size = tree.size().to_int_size();
        let scale_x = size as f32 / pixmap_size.width() as f32;
        let scale_y = size as f32 / pixmap_size.height() as f32;
        let scale = scale_x.min(scale_y);
        let scaled_width = (pixmap_size.width() as f32 * scale) as u32;
        let scaled_height = (pixmap_size.height() as f32 * scale) as u32;

        let mut pixmap = resvg::tiny_skia::Pixmap::new(scaled_width, scaled_height)?;
        resvg::render(
            &tree,
            resvg::tiny_skia::Transform::from_scale(scale, scale),
            &mut pixmap.as_mut(),
        );

        Some(egui::ColorImage::from_rgba_unmultiplied(
            [scaled_width as usize, scaled_height as usize],
            pixmap.data(),
        ))
    }

    fn get_embedded_svg(provider_id: &str) -> Option<&'static [u8]> {
        match provider_id {
            "github-copilot" => Some(include_bytes!(
                "../../../AIConsumptionTracker.UI/Assets/ProviderLogos/github.svg"
            )),
            "openai" => Some(include_bytes!(
                "../../../AIConsumptionTracker.UI/Assets/ProviderLogos/openai.svg"
            )),
            "anthropic" | "claude-code" => Some(include_bytes!(
                "../../../AIConsumptionTracker.UI/Assets/ProviderLogos/anthropic.svg"
            )),
            "deepseek" => Some(include_bytes!(
                "../../../AIConsumptionTracker.UI/Assets/ProviderLogos/deepseek.svg"
            )),
            "gemini-cli" | "google" => Some(include_bytes!(
                "../../../AIConsumptionTracker.UI/Assets/ProviderLogos/google.svg"
            )),
            "kimi" => Some(include_bytes!(
                "../../../AIConsumptionTracker.UI/Assets/ProviderLogos/kimi.svg"
            )),
            "minimax" => Some(include_bytes!(
                "../../../AIConsumptionTracker.UI/Assets/ProviderLogos/minimax.svg"
            )),
            "xiaomi" => Some(include_bytes!(
                "../../../AIConsumptionTracker.UI/Assets/ProviderLogos/xiaomi.svg"
            )),
            "mistral" => Some(include_bytes!(
                "../../../AIConsumptionTracker.UI/Assets/ProviderLogos/mistral.svg"
            )),
            "zai" | "zai-coding-plan" => Some(include_bytes!(
                "../../../AIConsumptionTracker.UI/Assets/ProviderLogos/zai.svg"
            )),
            _ => None,
        }
    }
}

impl Default for ProviderIcons {
    fn default() -> Self {
        Self::new()
    }
}
