let currentData = {
    summary: null,
    providers: [],
    history: [],
    dailyUsage: [],
    sortBy: 'name',
    searchQuery: '',
    providerFilter: '',
    periodFilter: 7
};

let autoRefreshInterval = null;

async function initializeDashboard() {
    await fetchSummary();
    await fetchProviders();
    await fetchHistory();
    await fetchDailyUsage();
    startAutoRefresh();
}

async function fetchSummary() {
    try {
        const response = await fetch('/api/summary');
        const summary = await response.json();
        currentData.summary = summary;

        document.getElementById('total-providers').textContent = summary.total_providers;
        document.getElementById('active-providers').textContent = summary.active_providers;
        document.getElementById('total-records').textContent = formatNumber(summary.total_records);
        document.getElementById('total-usage').textContent = formatUsage(summary.total_usage);
        document.getElementById('avg-daily-usage').textContent = summary.avg_daily_usage 
            ? formatUsage(summary.avg_daily_usage) 
            : '-';

        if (summary.last_updated) {
            document.getElementById('last-updated').textContent = `Last updated: ${formatDateTime(summary.last_updated)}`;
        }
    } catch (error) {
        console.error('Failed to fetch summary:', error);
    }
}

async function fetchProviders() {
    try {
        const response = await fetch('/api/providers');
        let providers = await response.json();
        currentData.providers = providers;
        renderProviders();
    } catch (error) {
        console.error('Failed to fetch providers:', error);
        document.getElementById('providers-grid').innerHTML = '<div class="loading">Error loading providers</div>';
    }
}

async function fetchHistory() {
    try {
        let url = '/api/history';
        const params = new URLSearchParams();

        if (currentData.providerFilter) {
            params.append('provider_id', currentData.providerFilter);
        }

        const endDate = new Date();
        const startDate = new Date();
        startDate.setDate(startDate.getDate() - currentData.periodFilter);

        params.append('end_date', startDate.toISOString());
        params.append('start_date', startDate.toISOString());
        params.append('limit', '50');

        url += '?' + params.toString();

        const response = await fetch(url);
        const history = await response.json();
        currentData.history = history;
        renderHistoryTable();
    } catch (error) {
        console.error('Failed to fetch history:', error);
    }
}

async function fetchDailyUsage() {
    try {
        let url = '/api/daily';
        const params = new URLSearchParams();

        if (currentData.providerFilter) {
            params.append('provider_id', currentData.providerFilter);
        }

        url += '?' + params.toString();

        const response = await fetch(url);
        const dailyUsage = await response.json();
        currentData.dailyUsage = dailyUsage;
        renderDailyChart();
    } catch (error) {
        console.error('Failed to fetch daily usage:', error);
    }
}

function renderProviders() {
    const container = document.getElementById('providers-grid');
    let providers = [...currentData.providers];

    if (currentData.searchQuery) {
        const query = currentData.searchQuery.toLowerCase();
        providers = providers.filter(p => 
            p.provider_name.toLowerCase().includes(query) ||
            p.provider_id.toLowerCase().includes(query)
        );
    }

    switch (currentData.sortBy) {
        case 'usage':
            providers.sort((a, b) => b.current_usage - a.current_usage);
            break;
        case 'records':
            providers.sort((a, b) => b.total_records - a.total_records);
            break;
        case 'name':
        default:
            providers.sort((a, b) => a.provider_name.localeCompare(b.provider_name));
    }

    container.innerHTML = providers.map(provider => createProviderCard(provider)).join('');
}

function createProviderCard(provider) {
    const percentage = provider.current_usage > 0 
        ? (provider.current_usage / (provider.limit || provider.current_usage) * 100).toFixed(1)
        : 0;
    
    const usageClass = percentage > 75 ? 'usage-high' 
                      : percentage > 50 ? 'usage-medium' 
                      : 'usage-low';
    
    return `
        <div class="provider-card" data-provider-id="${provider.provider_id}">
            <div class="provider-name">${provider.provider_name}</div>
            <div class="provider-stats">
                <div class="stat-row">
                    <span class="stat-label">Current Usage</span>
                    <span class="stat-value">${formatUsage(provider.current_usage)}</span>
                </div>
                <div class="stat-row">
                    <span class="stat-label">Last Updated</span>
                    <span class="stat-value">${formatDateTime(provider.last_updated)}</span>
                </div>
                <div class="stat-row">
                    <span class="stat-label">Usage Unit</span>
                    <span class="stat-value">${provider.usage_unit || '-'}</span>
                </div>
                ${provider.limit ? `
                <div class="usage-bar">
                    <div class="usage-fill ${usageClass}" style="width: ${percentage}%"></div>
                </div>
                <div class="stat-row">
                    <span class="stat-label">Usage</span>
                    <span class="stat-value">${percentage}%</span>
                    <span class="stat-label">${provider.usage_unit}</span>
                </div>
                ` : ''}
            </div>
        </div>
    `;
}

function renderHistoryTable() {
    const tbody = document.querySelector('#history-table tbody');
    tbody.innerHTML = currentData.history.map(record => `
        <tr>
            <td>${formatDateTime(record.timestamp)}</td>
            <td>${record.provider_name}</td>
            <td>${formatUsage(record.usage)}</td>
            <td>${record.limit ? formatUsage(record.limit) : '-'}</td>
            <td>${record.usage_unit}</td>
            <td>
                <span class="trend-badge ${record.is_quota_based ? 'trend-stable' : 'trend-increasing'}">
                    ${record.is_quota_based ? 'Quota' : 'Pay-As-You-Go'}
                </span>
            </td>
        </tr>
    `).join('');
}

function renderDailyChart() {
    const canvas = document.getElementById('daily-chart');
    const ctx = canvas.getContext('2d');

    if (currentData.dailyUsage.length === 0) {
        ctx.fillStyle = '#a0a0a0';
        ctx.font = '16px sans-serif';
        ctx.textAlign = 'center';
        ctx.fillText('No data available', canvas.width / 2, canvas.height / 2);
        return;
    }

    const sortedData = [...currentData.dailyUsage].reverse();

    const values = sortedData.map(d => d.total_usage);
    const maxValue = Math.max(...values);
    const minValue = Math.min(...values);

    ctx.clearRect(0, 0, canvas.width, canvas.height);

    const padding = 60;
    const chartWidth = canvas.width - padding * 2;
    const chartHeight = canvas.height - padding * 2;
    const barWidth = chartWidth / sortedData.length - 10;
    const gap = 10;

    sortedData.forEach((day, index) => {
        const x = padding + index * (barWidth + gap);
        const barHeight = ((day.total_usage - minValue) / (maxValue - minValue)) * chartHeight;
        const y = canvas.height - padding - barHeight;

        const gradient = ctx.createLinearGradient(x, y, x, y + barHeight);
        gradient.addColorStop(0, '#5865f2');
        gradient.addColorStop(1, '#6b7280');

        ctx.fillStyle = gradient;
        ctx.fillRect(x, y, barWidth, barHeight);

        ctx.fillStyle = '#e0e0e0';
        ctx.font = '12px sans-serif';
        ctx.textAlign = 'center';
        ctx.fillText(formatUsage(day.total_usage), x + barWidth / 2, y - 10);

        ctx.fillStyle = '#a0a0a0';
        ctx.fillText(day.date, x + barWidth / 2, canvas.height - padding + 20);
    });

    ctx.fillStyle = '#a0a0a0';
    ctx.font = '14px sans-serif';
    ctx.textAlign = 'left';
    ctx.fillText(formatUsage(minValue), padding - 50, padding);
    ctx.fillText(formatUsage(maxValue), padding - 50, canvas.height - padding);
}

function formatNumber(num) {
    if (num >= 1000000) {
        return (num / 1000000).toFixed(1) + 'M';
    } else if (num >= 1000) {
        return (num / 1000).toFixed(1) + 'K';
    }
    return num.toLocaleString();
}

function formatUsage(value) {
    if (value >= 1000000) {
        return (value / 1000000).toFixed(2) + 'M';
    } else if (value >= 1000) {
        return (value / 1000).toFixed(2) + 'K';
    } else if (value >= 1) {
        return value.toFixed(2);
    } else {
        return value.toFixed(3);
    }
}

function formatDateTime(timestamp) {
    const date = new Date(timestamp);
    return date.toLocaleString('en-US', {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
    });
}

function startAutoRefresh() {
    stopAutoRefresh();
    autoRefreshInterval = setInterval(async () => {
        await fetchSummary();
        await fetchProviders();
    }, 60000);
}

function stopAutoRefresh() {
    if (autoRefreshInterval) {
        clearInterval(autoRefreshInterval);
        autoRefreshInterval = null;
    }
}

document.getElementById('sort-by').addEventListener('change', (e) => {
    currentData.sortBy = e.target.value;
    renderProviders();
});

document.getElementById('search-provider').addEventListener('input', (e) => {
    currentData.searchQuery = e.target.value;
    renderProviders();
});

document.getElementById('provider-filter').addEventListener('change', async (e) => {
    currentData.providerFilter = e.target.value;
    await fetchHistory();
    await fetchDailyUsage();
});

document.getElementById('period-filter').addEventListener('change', async (e) => {
    currentData.periodFilter = parseInt(e.target.value);
    await fetchHistory();
});

document.addEventListener('DOMContentLoaded', initializeDashboard);
document.addEventListener('visibilitychange', () => {
    if (document.hidden) {
        stopAutoRefresh();
    } else {
        startAutoRefresh();
    }
});
