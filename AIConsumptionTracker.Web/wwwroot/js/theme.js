// Theme Management System with HTMX Support
// AI Consumption Tracker Web UI

const ThemeManager = {
    // Available themes
    themes: [
        { id: 'dark', name: 'Dark', icon: '🌙' },
        { id: 'light', name: 'Light', icon: '☀️' },
        { id: 'high-contrast', name: 'High Contrast', icon: '👁️' },
        { id: 'solarized-dark', name: 'Solarized Dark', icon: '🌆' },
        { id: 'solarized-light', name: 'Solarized Light', icon: '🌅' },
        { id: 'dracula', name: 'Dracula', icon: '🧛' },
        { id: 'nord', name: 'Nord', icon: '❄️' }
    ],

    // Initialize theme system
    init() {
        this.loadSavedTheme();
        this.setupThemeToggle();
        this.setupHTMXListeners();
        this.applyTheme(this.getCurrentTheme());
    },

    // Get current theme from localStorage or default
    getCurrentTheme() {
        return localStorage.getItem('theme') || 'dark';
    },

    // Apply theme to document
    applyTheme(themeId) {
        const html = document.documentElement;
        html.setAttribute('data-theme', themeId);
        this.updateToggleButton(themeId);
        localStorage.setItem('theme', themeId);
        
        // Dispatch custom event for other components
        window.dispatchEvent(new CustomEvent('themeChanged', { detail: { theme: themeId } }));
    },

    // Toggle between themes (cycles through all)
    toggleTheme() {
        const currentTheme = this.getCurrentTheme();
        const currentIndex = this.themes.findIndex(t => t.id === currentTheme);
        const nextIndex = (currentIndex + 1) % this.themes.length;
        const nextTheme = this.themes[nextIndex];
        
        this.applyTheme(nextTheme.id);
        
        // Show notification
        this.showNotification(`Theme: ${nextTheme.name}`);
    },

    // Set specific theme
    setTheme(themeId) {
        if (this.themes.find(t => t.id === themeId)) {
            this.applyTheme(themeId);
        }
    },

    // Update toggle button icon
    updateToggleButton(themeId) {
        const theme = this.themes.find(t => t.id === themeId);
        const iconElement = document.getElementById('theme-icon');
        const btnElement = document.getElementById('theme-toggle-btn');
        
        if (iconElement && theme) {
            iconElement.textContent = theme.icon;
        }
        
        if (btnElement) {
            btnElement.title = `Current theme: ${theme?.name || 'Dark'}. Click to cycle themes.`;
        }
    },

    // Load saved theme on page load
    loadSavedTheme() {
        const savedTheme = localStorage.getItem('theme');
        if (savedTheme) {
            document.documentElement.setAttribute('data-theme', savedTheme);
        }
    },

    // Setup theme toggle button
    setupThemeToggle() {
        const toggleBtn = document.getElementById('theme-toggle-btn');
        if (toggleBtn) {
            // Use data-action attribute for event binding
            toggleBtn.addEventListener('click', (e) => {
                e.preventDefault();
                this.toggleTheme();
            });
        }
        
        // Update initial button state
        this.updateToggleButton(this.getCurrentTheme());
    },

    // Setup HTMX event listeners for theme persistence across swaps
    setupHTMXListeners() {
        // Re-apply theme after HTMX content swaps
        document.addEventListener('htmx:afterSwap', () => {
            this.applyTheme(this.getCurrentTheme());
        });
        
        // Apply theme on initial load
        document.addEventListener('DOMContentLoaded', () => {
            this.applyTheme(this.getCurrentTheme());
        });
    },

    // Show temporary notification
    showNotification(message) {
        // Remove existing notification
        const existing = document.getElementById('theme-notification');
        if (existing) {
            existing.remove();
        }
        
        // Create new notification
        const notification = document.createElement('div');
        notification.id = 'theme-notification';
        notification.style.cssText = `
            position: fixed;
            bottom: 20px;
            right: 20px;
            background-color: var(--bg-secondary);
            color: var(--text-primary);
            padding: 12px 20px;
            border-radius: 8px;
            border: 1px solid var(--border-color);
            box-shadow: 0 4px 12px var(--shadow-color);
            z-index: 10000;
            font-size: 14px;
            opacity: 0;
            transform: translateY(20px);
            transition: opacity 0.3s, transform 0.3s;
        `;
        notification.textContent = message;
        
        document.body.appendChild(notification);
        
        // Animate in
        requestAnimationFrame(() => {
            notification.style.opacity = '1';
            notification.style.transform = 'translateY(0)';
        });
        
        // Remove after delay
        setTimeout(() => {
            notification.style.opacity = '0';
            notification.style.transform = 'translateY(20px)';
            setTimeout(() => notification.remove(), 300);
        }, 2000);
    },

    // Create theme selector dropdown (can be called to create a dropdown menu)
    createThemeSelector() {
        const dropdown = document.createElement('div');
        dropdown.className = 'theme-dropdown';
        dropdown.id = 'theme-dropdown';
        
        this.themes.forEach(theme => {
            const option = document.createElement('div');
            option.className = 'theme-option';
            option.dataset.theme = theme.id;
            option.innerHTML = `${theme.icon} ${theme.name}`;
            
            if (theme.id === this.getCurrentTheme()) {
                option.classList.add('active');
            }
            
            option.addEventListener('click', () => {
                this.setTheme(theme.id);
                dropdown.classList.remove('show');
            });
            
            dropdown.appendChild(option);
        });
        
        return dropdown;
    }
};

// Initialize on DOM ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => ThemeManager.init());
} else {
    ThemeManager.init();
}

// Expose to global scope for debugging
window.ThemeManager = ThemeManager;
