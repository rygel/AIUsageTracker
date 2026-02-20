// Theme Management - Business Edition
const ThemeManager = {
    themes: ['dark', 'light', 'corporate', 'midnight'],
    
    init() {
        const saved = localStorage.getItem('theme');
        if (saved && this.themes.includes(saved)) {
            document.documentElement.setAttribute('data-theme', saved);
        }
        this.setupSelect();
    },
    
    setupSelect() {
        const select = document.getElementById('theme-select');
        if (!select) return;
        
        const current = document.documentElement.getAttribute('data-theme') || 'dark';
        select.value = current;
        
        select.addEventListener('change', (e) => {
            const theme = e.target.value;
            document.documentElement.setAttribute('data-theme', theme);
            localStorage.setItem('theme', theme);
        });
    }
};

document.addEventListener('DOMContentLoaded', () => ThemeManager.init());
window.ThemeManager = ThemeManager;
