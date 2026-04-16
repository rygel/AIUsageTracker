// Theme Management
const ThemeManager = {
    // GENERATED-THEME-LIST-START
    themes: [
        'dark',
        'light',
        'corporate',
        'midnight',
        'dracula',
        'nord',
        'monokai',
        'one-dark',
        'solarized-dark',
        'solarized-light',
        'catppuccin-frappe',
        'catppuccin-macchiato',
        'catppuccin-mocha',
        'catppuccin-latte'
    ],
    // GENERATED-THEME-LIST-END
    
    init() {
        const saved = localStorage.getItem('theme');
        if (saved && this.themes.includes(saved)) {
            document.documentElement.dataset.theme = saved;
        }
        this.setupSelect();
    },
    
    setupSelect() {
        const select = document.getElementById('theme-select');
        if (!select) return;
        
        const current = document.documentElement.dataset.theme || 'dark';
        select.value = current;
        
        select.addEventListener('change', (e) => {
            const theme = e.target.value;
            document.documentElement.dataset.theme = theme;
            localStorage.setItem('theme', theme);
        });
    }
};

document.addEventListener('DOMContentLoaded', () => ThemeManager.init());
globalThis.ThemeManager = ThemeManager;
