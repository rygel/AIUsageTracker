use std::sync::{Arc, Mutex};

#[derive(Clone, Debug)]
pub enum TrayEvent {
    Show,
    Quit,
}

pub struct TrayManager {
    event_receiver: Arc<Mutex<Option<std::sync::mpsc::Receiver<TrayEvent>>>>,
    initialized: bool,
}

impl TrayManager {
    pub fn new() -> Self {
        Self {
            event_receiver: Arc::new(Mutex::new(None)),
            initialized: false,
        }
    }

    pub fn initialize(&mut self) -> Result<std::sync::mpsc::Receiver<TrayEvent>, String> {
        if self.initialized {
            return Err("Tray already initialized".to_string());
        }

        let (tx, rx) = std::sync::mpsc::channel();

        #[cfg(target_os = "windows")]
        {
            use tray_item::{IconSource, TrayItem};

            let mut tray =
                TrayItem::new("AI Consumption Tracker", IconSource::Resource("app-icon"))
                    .map_err(|e| format!("Failed to create tray: {:?}", e))?;

            let tx_show = tx.clone();
            tray.add_menu_item("Show", move || {
                let _ = tx_show.send(TrayEvent::Show);
            })
            .map_err(|e| format!("Failed to add menu item: {:?}", e))?;

            let tx_quit = tx.clone();
            tray.add_menu_item("Quit", move || {
                let _ = tx_quit.send(TrayEvent::Quit);
            })
            .map_err(|e| format!("Failed to add menu item: {:?}", e))?;

            std::mem::forget(tray);
        }

        #[cfg(target_os = "linux")]
        {
            use tray_item::{IconSource, TrayItem};

            let mut tray =
                TrayItem::new("AI Consumption Tracker", IconSource::Resource("app-icon"))
                    .map_err(|e| format!("Failed to create tray: {:?}", e))?;

            let tx_show = tx.clone();
            tray.add_menu_item("Show", move || {
                let _ = tx_show.send(TrayEvent::Show);
            })
            .map_err(|e| format!("Failed to add menu item: {:?}", e))?;

            let tx_quit = tx.clone();
            tray.add_menu_item("Quit", move || {
                let _ = tx_quit.send(TrayEvent::Quit);
            })
            .map_err(|e| format!("Failed to add menu item: {:?}", e))?;

            std::mem::forget(tray);
        }

        #[cfg(target_os = "linux")]
        {
            use tray_item::{IconSource, TrayItem};

            let mut tray = TrayItem::new(
                "AI Consumption Tracker",
                IconSource::Data(include_bytes!("../icons/32x32.png")),
            )
            .map_err(|e| format!("Failed to create tray: {:?}", e))?;

            let tx_show = tx.clone();
            tray.add_menu_item("Show", move || {
                let _ = tx_show.send(TrayEvent::Show);
            })
            .map_err(|e| format!("Failed to add menu item: {:?}", e))?;

            let tx_quit = tx.clone();
            tray.add_menu_item("Quit", move || {
                let _ = tx_quit.send(TrayEvent::Quit);
            })
            .map_err(|e| format!("Failed to add menu item: {:?}", e))?;

            std::mem::forget(tray);
        }

        #[cfg(target_os = "macos")]
        {
            let _ = tx;
        }

        self.initialized = true;
        Ok(rx)
    }

    pub fn poll_event(&self) -> Option<TrayEvent> {
        if let Ok(guard) = self.event_receiver.try_lock() {
            if let Some(ref rx) = *guard {
                return rx.try_recv().ok();
            }
        }
        None
    }
}

impl Default for TrayManager {
    fn default() -> Self {
        Self::new()
    }
}

impl Drop for TrayManager {
    fn drop(&mut self) {}
}
