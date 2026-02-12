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

let chartZoomLevel = 1.0;
let chartPanOffset = 0;
let isDragging = false;
let dragStartX = 0;
let dragStartPan = 0;

async function initializeDashboard() {
    await fetchSummary();
    await fetchProviders();
    await fetchHistory();
    await fetchDailyUsage();
    setupProviderChartFilter();
    setupDailyUsage();
    startAutoRefresh();
}

function setupProviderChartFilter() {
    const select = document.getElementById('chart-provider-filter');
    select.addEventListener('change', async (e) => {
        const providerId = e.target.value;
        if (providerId) {
            await renderProviderChart(providerId);
        } else {
            clearProviderChart();
        }
    });

    const timeRangeSelect = document.getElementById('chart-time-range');
    if (timeRangeSelect) {
        timeRangeSelect.addEventListener('change', async () => {
            const providerId = document.getElementById('chart-provider-filter').value;
            if (providerId) {
                resetChartZoom();
                await renderProviderChart(providerId);
            }
        });
    }

    const zoomInBtn = document.getElementById('zoom-in');
    if (zoomInBtn) {
        zoomInBtn.addEventListener('click', () => {
            updateChartZoom(0.25);
        });
    }

    const zoomOutBtn = document.getElementById('zoom-out');
    if (zoomOutBtn) {
        zoomOutBtn.addEventListener('click', () => {
            updateChartZoom(-0.25);
        });
    }

    const zoomResetBtn = document.getElementById('zoom-reset');
    if (zoomResetBtn) {
        zoomResetBtn.addEventListener('click', () => {
            resetChartZoom();
        });
    }

    const canvas = document.getElementById('provider-usage-chart');
    if (canvas) {
        canvas.addEventListener('mousedown', (e) => {
            isDragging = true;
            dragStartX = e.clientX;
            dragStartPan = chartPanOffset;
            canvas.style.cursor = 'grabbing';
        });

        canvas.addEventListener('mousemove', (e) => {
            if (!isDragging) return;
            const dx = e.clientX - dragStartX;
            const canvasWidth = canvas.offsetWidth || 800;
            const chartWidth = canvasWidth - 120;
            chartPanOffset = dragStartPan - (dx / chartWidth) * chartZoomLevel;
            chartPanOffset = Math.max(0, Math.min(chartPanOffset, chartZoomLevel - 1));
            const providerId = document.getElementById('chart-provider-filter').value;
            if (providerId) {
                renderProviderChart(providerId);
            }
        });

        canvas.addEventListener('mouseup', () => {
            isDragging = false;
            canvas.style.cursor = 'grab';
        });

        canvas.addEventListener('mouseleave', () => {
            isDragging = false;
            canvas.style.cursor = 'grab';
        });

        canvas.style.cursor = 'grab';
    }
}

function updateProviderChartOptions() {
    const select = document.getElementById('chart-provider-filter');
    const currentValue = select.value;
    
    // Clear existing options except the first one
    while (select.options.length > 1) {
        select.remove(1);
    }
    
    // Add providers from current data
    currentData.providers.forEach(provider => {
        const option = document.createElement('option');
        option.value = provider.provider_id;
        option.textContent = provider.provider_name;
        select.appendChild(option);
    });
    
    // Restore previous selection if still valid
    if (currentValue) {
        select.value = currentValue;
    }
}

async function renderProviderChart(providerId) {
    try {
        // Fetch history from API with appropriate limit
        const timeRangeSelect = document.getElementById('chart-time-range');
        const periodFilter = timeRangeSelect ? parseInt(timeRangeSelect.value) : 7;
        const limit = periodFilter > 30 ? 500 : 100;
        
        const response = await fetch(`/api/history?provider_id=${providerId}&limit=${limit}`);
        const history = await response.json();

        if (history.length === 0) {
            clearProviderChart();
            return;
        }

        // Sort by timestamp ascending for the chart
        const sortedHistory = [...history].reverse();

        const canvas = document.getElementById('provider-usage-chart');
        const ctx = canvas.getContext('2d');

        // Set canvas size
        canvas.width = canvas.offsetWidth || 800;
        canvas.height = 400;

        const padding = 60;
        const chartWidth = canvas.width - padding * 2;
        const chartHeight = canvas.height - padding * 2;

        // Clear canvas
        ctx.clearRect(0, 0, canvas.width, canvas.height);

        // Get data points
        const allDataPoints = sortedHistory.map(h => ({
            timestamp: new Date(h.timestamp),
            usage: h.usage,
            limit: h.limit
        }));

        // Apply zoom and pan to determine visible data range
        const { startIndex, endIndex } = calculateVisibleRange(allDataPoints.length);
        const dataPoints = allDataPoints.slice(startIndex, endIndex + 1);

        // Get time range from visible data
        const minTime = dataPoints[0].timestamp.getTime();
        const maxTime = dataPoints[dataPoints.length - 1].timestamp.getTime();
        const timeRange = maxTime - minTime || 1;

        // Helper function to calculate X position based on timestamp
        const getXFromTimestamp = (timestamp) => {
            const time = timestamp.getTime();
            const ratio = (time - minTime) / timeRange;
            return padding + ratio * chartWidth;
        };

        // Calculate scales
        const maxUsage = Math.max(...dataPoints.map(d => Math.max(d.usage, d.limit || 0)));
        const minUsage = Math.min(...dataPoints.map(d => d.usage));
        const usageRange = maxUsage - minUsage || 1;

        // Draw axes
        ctx.strokeStyle = '#444';
        ctx.lineWidth = 2;
        ctx.beginPath();
        // Y axis
        ctx.moveTo(padding, padding);
        ctx.lineTo(padding, canvas.height - padding);
        // X axis
        ctx.lineTo(canvas.width - padding, canvas.height - padding);
        ctx.stroke();

        // Draw Y axis labels
        ctx.fillStyle = '#888';
        ctx.font = '12px sans-serif';
        ctx.textAlign = 'right';
        for (let i = 0; i <= 5; i++) {
            const y = padding + (chartHeight * i / 5);
            const value = maxUsage - (usageRange * i / 5);
            ctx.fillText(formatUsage(value), padding - 10, y + 4);

            // Grid line
            ctx.strokeStyle = '#333';
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.moveTo(padding, y);
            ctx.lineTo(canvas.width - padding, y);
            ctx.stroke();
        }

        // Extract unique reset times from history data
        const resetTimes = new Set();
        sortedHistory.forEach(h => {
            if (h.next_reset_time) {
                resetTimes.add(h.next_reset_time);
            }
        });

        // Draw reset event vertical lines (BEHIND the usage line)
        resetTimes.forEach(resetTimeStr => {
            const resetTime = new Date(resetTimeStr);
            const x = getXFromTimestamp(resetTime);

            // Only draw if within chart bounds
            if (x >= padding && x <= canvas.width - padding) {
                // Draw vertical yellow dashed line
                ctx.strokeStyle = '#ffd93d';
                ctx.lineWidth = 2;
                ctx.setLineDash([8, 4]);
                ctx.beginPath();
                ctx.moveTo(x, padding);
                ctx.lineTo(x, canvas.height - padding);
                ctx.stroke();
                ctx.setLineDash([]);

                // Draw reset label at top, rotated 45 degrees
                ctx.save();
                ctx.translate(x, padding + 5);
                ctx.rotate(45 * Math.PI / 180);
                ctx.fillStyle = '#ffd93d';
                ctx.font = 'bold 10px sans-serif';
                ctx.textAlign = 'left';
                // Show the time in HH:MM format
                const timeLabel = resetTime.toLocaleTimeString('en-US', {
                    hour: '2-digit',
                    minute: '2-digit'
                });
                ctx.fillText(`RESET ${timeLabel}`, 5, 0);
                ctx.restore();
            }
        });
        
        // Draw usage line
        if (dataPoints.length > 1) {
            ctx.strokeStyle = '#5865f2';
            ctx.lineWidth = 3;
            ctx.beginPath();
            
            dataPoints.forEach((point, index) => {
                const x = getXFromTimestamp(point.timestamp);
                const y = padding + ((maxUsage - point.usage) / usageRange) * chartHeight;
                
                if (index === 0) {
                    ctx.moveTo(x, y);
                } else {
                    ctx.lineTo(x, y);
                }
            });
            
            ctx.stroke();
            
            // Draw points
            dataPoints.forEach((point) => {
                const x = getXFromTimestamp(point.timestamp);
                const y = padding + ((maxUsage - point.usage) / usageRange) * chartHeight;
                
                ctx.fillStyle = '#5865f2';
                ctx.beginPath();
                ctx.arc(x, y, 4, 0, Math.PI * 2);
                ctx.fill();
            });
        }
        
        // Draw limit line if available
        const pointsWithLimit = dataPoints.filter(d => d.limit);
        if (pointsWithLimit.length > 0) {
            const limit = pointsWithLimit[0].limit;
            const y = padding + ((maxUsage - limit) / usageRange) * chartHeight;
            
            ctx.strokeStyle = '#ff6b6b';
            ctx.lineWidth = 2;
            ctx.setLineDash([5, 5]);
            ctx.beginPath();
            ctx.moveTo(padding, y);
            ctx.lineTo(canvas.width - padding, y);
            ctx.stroke();
            ctx.setLineDash([]);
            
            // Label
            ctx.fillStyle = '#ff6b6b';
            ctx.font = '12px sans-serif';
            ctx.textAlign = 'left';
            ctx.fillText(`Limit: ${formatUsage(limit)}`, canvas.width - padding + 5, y + 4);
        }
        
        // Draw X axis labels (timestamps)
        ctx.fillStyle = '#888';
        ctx.font = '10px sans-serif';
        ctx.textAlign = 'center';
        
        const labelCount = Math.min(6, dataPoints.length);
        for (let i = 0; i < labelCount; i++) {
            const index = Math.floor(i * (dataPoints.length - 1) / (labelCount - 1));
            const point = dataPoints[index];
            const x = getXFromTimestamp(point.timestamp);
            
            const timeStr = point.timestamp.toLocaleTimeString('en-US', {
                hour: '2-digit',
                minute: '2-digit'
            });
            ctx.fillText(timeStr, x, canvas.height - padding + 20);
        }
        
        // Title with zoom level indicator
        const providerName = currentData.providers.find(p => p.provider_id === providerId)?.provider_name || providerId;
        ctx.fillStyle = '#e0e0e0';
        ctx.font = 'bold 14px sans-serif';
        ctx.textAlign = 'center';
        const zoomText = chartZoomLevel > 1 ? ` (Zoom: ${chartZoomLevel.toFixed(1)}x)` : '';
        ctx.fillText(`${providerName} - Usage History${zoomText}`, canvas.width / 2, 30);
        
    } catch (error) {
        console.error('Failed to render provider chart:', error);
    }
}

function clearProviderChart() {
    const canvas = document.getElementById('provider-usage-chart');
    const ctx = canvas.getContext('2d');
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    
    ctx.fillStyle = '#666';
    ctx.font = '14px sans-serif';
    ctx.textAlign = 'center';
    ctx.fillText('Select a provider to view usage history', canvas.width / 2, canvas.height / 2);
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
        updateProviderChartOptions();
        updateHistoryProviderFilter();
    } catch (error) {
        console.error('Failed to fetch providers:', error);
        document.getElementById('providers-grid').innerHTML = '<div class="loading">Error loading providers</div>';
    }
}

function updateHistoryProviderFilter() {
    const select = document.getElementById('provider-filter');
    const currentValue = select.value;
    
    // Clear existing options except the first one (All Providers)
    while (select.options.length > 1) {
        select.remove(1);
    }
    
    // Add providers from current data
    currentData.providers.forEach(provider => {
        const option = document.createElement('option');
        option.value = provider.provider_id;
        option.textContent = provider.provider_name;
        select.appendChild(option);
    });
    
    // Restore previous selection if still valid
    if (currentValue) {
        select.value = currentValue;
    }
}

async function fetchHistory() {
    try {
        let url = '/api/history';
        const params = new URLSearchParams();

        if (currentData.providerFilter) {
            params.append('provider_id', currentData.providerFilter);
        }

        // Get time range from chart selector if available, otherwise use currentData.periodFilter
        const timeRangeSelect = document.getElementById('chart-time-range');
        const periodFilter = timeRangeSelect ? parseInt(timeRangeSelect.value) : currentData.periodFilter;

        // Calculate date range
        const endDate = new Date();
        const startDate = new Date();
        startDate.setDate(startDate.getDate() - periodFilter);

        params.append('end_date', endDate.toISOString());
        params.append('start_date', startDate.toISOString());
        
        // Fetch more data points for longer time ranges
        const limit = periodFilter > 30 ? 500 : 100;
        params.append('limit', limit.toString());

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
        let limit = document.getElementById('daily-limit')?.value || '30';
        let response = await fetch(`/api/daily?limit=${limit}`);
        const dailyUsage = await response.json();
        currentData.dailyUsage = dailyUsage;
        renderDailyUsageTable();
        renderDailyUsageChart();
    } catch (error) {
        console.error('Failed to fetch daily usage:', error);
        // Provide fallback data
        const today = new Date().toISOString().split('T')[0];
        // Default to show last 7 days
        currentData.dailyUsage = [{
            date: today,
            total_usage: 50,
            record_count: 3
        }];
    }
}

function renderDailyUsageTable() {
    const tbody = document.getElementById('daily-usage-tbody');
    const data = currentData.dailyUsage || [];
    
    if (data.length === 0) {
        tbody.innerHTML = '<tr><td colspan="4" class="loading">Loading daily usage data...</td></tr>';
        return;
    }
    
    tbody.innerHTML = data.map(d => {
        const activeDays = Math.ceil(Math.max(1, 30));
        const avgUsage = d.record_count > 0 ? (d.total_usage / d.record_count).toFixed(1) : 'N/A';
        
        return `<tr>
            <td>${d.date}</td>
            <td>${d.total_usage.toFixed(1)}</td>
            <td>${d.record_count}</td>
            <td>${activeDays}</td>
            <td class="metric-value">${avgUsage}</td>
        </tr>`;
    }).join('');
}

function renderDailyUsageChart() {
        // Daily Usage Chart
        const dailyCtx = document.getElementById('daily-usage-chart');
        if (!dailyCtx) return;
        const dailyData = currentData.dailyUsage || [];
        if (dailyData.length === 0) {
            dailyCtx.fillStyle = 'rgba(255, 255, 255, 0.5)';
            dailyCtx.font = '16px sans-serif';
            dailyCtx.textAlign = 'center';
            dailyCtx.fillText('No daily usage data available', dailyCtx.width / 2, dailyCtx.height / 2);
            return;
        }
        
        const chartType = dailyData.some(d => d.total_usage > 0) ? 'usage' : 'records';
        dailyCtx.strokeStyle = 'rgba(91, 153, 213, 1)';
        dailyCtx.lineWidth = 2;
        dailyCtx.beginPath();
        
        // Get time range inputs
        const startDateInput = document.getElementById('date-range-start');
        const endDateInput = document.getElementById('date-range-end');
        const applyButton = document.getElementById('apply-date-range');
        
        // Add event listeners for custom date ranges
        if (startDateInput && endDateInput && applyButton) {
            const applyDateRange = () => {
                const startDate = startDateInput.value;
                const endDate = endDateInput.value;
                const dateRange = {
                    startDate: startDate,
                    endDate: endDate
                };
                
                applyButton.addEventListener('click', async () => {
                    await fetchDailyUsage(dateRange);
                    // Update dropdown to reflect custom range
                    dailyLimitSelect.value = '999';
                });
        }
        
        // Update the dropdown when custom range is applied
        if (applyButton) {
            applyDateRange();
        }
        
        // Draw axes and data points based on chart type
        if (chartType === 'usage') {
            // Usage chart
            const maxUsage = Math.max(...dailyData.map(d => d.total_usage));
            const minUsage = Math.min(...dailyData.map(d => d.total_usage));
            const range = maxUsage - minUsage || 1;
            
            // Y-axis
            dailyCtx.fillStyle = '#888';
            dailyCtx.font = '12px sans-serif';
            dailyCtx.textAlign = 'right';
            for (let i = 0; i <= 5; i++) {
                const value = minUsage + (range * (5 - i) / 5);
                const y = dailyCtx.height - 40 - (i * 30);
                dailyCtx.fillText(value.toFixed(1), dailyCtx.width - 10, y + 15);
                
                if (i > 0) {
                    dailyCtx.strokeStyle = '#ddd';
                    dailyCtx.beginPath();
                    dailyCtx.moveTo(dailyCtx.width - 40, y + 15);
                    dailyCtx.lineTo(dailyCtx.width - 10, y + 15);
                    dailyCtx.stroke();
                }
            }
            
            // X-axis
            dailyCtx.textAlign = 'center';
            dailyData.forEach((data, index) => {
                const x = 30 + (index * (dailyCtx.width - 60) / (dailyData.length - 1));
                const y = dailyCtx.height - 40 - (data.total_usage / range) * 100;
                
                // Usage bar
                const barWidth = 20;
                const barHeight = Math.max(2, (data.total_usage / range) * 100);
                const barY = y - barHeight / 2;
                
                dailyCtx.fillStyle = data.total_usage > 0 ? 'rgba(67, 160, 71, 1)' : 'rgba(255, 107, 107, 0.2)';
                dailyCtx.fillRect(x - barWidth/2, barY, barWidth, barHeight);
                
                // Date label
                const date = new Date(data.date);
                dailyCtx.fillStyle = '#ddd';
                dailyCtx.textAlign = 'center';
                dailyCtx.fillText(date.toLocaleDateString(), x, barY - 5);
            });
            
            // Title
            dailyCtx.fillStyle = '#333';
            dailyCtx.font = 'bold 16px sans-serif';
            dailyCtx.textAlign = 'center';
            dailyCtx.fillText('Daily Usage', dailyCtx.width / 2, 30);
        } else {
            // Records count chart
            const maxRecords = Math.max(...dailyData.map(d => d.record_count));
            const minRecords = Math.min(...dailyData.map(d => d.record_count));
            const range = maxRecords - minRecords || 1;
            
            // Y-axis
            dailyCtx.fillStyle = '#888';
            dailyCtx.font = '12px sans-serif';
            dailyCtx.textAlign = 'right';
            for (let i = 0; i <= 5; i++) {
                const value = minRecords + (range * (5 - i) / 5);
                const y = dailyCtx.height - 40 - (i * 30);
                dailyCtx.fillText(value.toFixed(0), dailyCtx.width - 10, y + 15);
                
                if (i > 0) {
                    dailyCtx.strokeStyle = '#ddd';
                    dailyCtx.beginPath();
                    dailyCtx.moveTo(dailyCtx.width - 40, y + 15);
                    dailyCtx.lineTo(dailyCtx.width - 10, y + 15);
                    dailyCtx.stroke();
                }
            }
            
            // X-axis
            dailyCtx.textAlign = 'center';
            dailyData.forEach((data, index) => {
                const x = 30 + (index * (dailyCtx.width - 60) / (dailyData.length - 1));
                const y = dailyCtx.height - 40 - (index * 30);
                dailyCtx.fillText(x, y + 15);
                
                // Records bar
                const barWidth = 20;
                const barHeight = Math.max(2, (data.record_count / range) * 100);
                const barY = y - barHeight / 2;
                
                dailyCtx.fillStyle = '#888';
                dailyCtx.fillRect(x - barWidth/2, barY, barWidth, barHeight);
                
                // Date label
                const date = new Date(data.date);
                dailyCtx.fillStyle = '#ddd';
                dailyCtx.textAlign = 'center';
                dailyCtx.fillText(date.toLocaleDateString(), x, barY - 5);
            });
            
            // Title
            dailyCtx.fillStyle = '#333';
            dailyCtx.font = 'bold 16px sans-serif';
            dailyCtx.textAlign = 'center';
            dailyCtx.fillText('Daily Activity', dailyCtx.width / 2, 30);
        }
    if (chartType === 'usage') {
        // Usage chart
        const maxUsage = Math.max(...dailyData.map(d => d.total_usage));
        const minUsage = Math.min(...dailyData.map(d => d.total_usage));
        const range = maxUsage - minUsage || 1;
        
        // Y-axis
        dailyCtx.fillStyle = '#888';
        dailyCtx.font = '12px sans-serif';
        dailyCtx.textAlign = 'right';
        for (let i = 0; i <= 5; i++) {
            const value = minUsage + (range * (5 - i) / 5);
                const y = dailyCtx.height - 40 - (i * 30);
                dailyCtx.fillText(value.toFixed(1), dailyCtx.width - 10, y + 15);
                
                if (i > 0) {
                    dailyCtx.strokeStyle = '#ddd';
                    dailyCtx.beginPath();
                    dailyCtx.moveTo(dailyCtx.width - 40, y + 15);
                    dailyCtx.lineTo(dailyCtx.width - 10, y);
                    dailyCtx.stroke();
                }
            }
            
            // X-axis
            dailyCtx.textAlign = 'center';
            dailyData.forEach((data, index) => {
                const x = 30 + (index * (dailyCtx.width - 60) / (dailyData.length - 1));
                const y = dailyCtx.height - 30;
                dailyCtx.fillText(x, y + 15);
                
                // Usage bar
                const barWidth = 20;
                const barHeight = Math.max(2, (data.total_usage / range) * 100);
                const barY = dailyCtx.height - 30 - barHeight;
                
                dailyCtx.fillStyle = data.total_usage > 0 ? 'rgba(67, 160, 71, 1)' : 'rgba(255, 107, 107, 0.2)';
                dailyCtx.fillRect(x - barWidth/2, barY, barWidth, barHeight);
                
                // Date label
                const date = new Date(data.date);
                dailyCtx.fillStyle = '#ddd';
                dailyCtx.textAlign = 'center';
                dailyCtx.fillText(date.toLocaleDateString(), x + 10, y - 5);
            });
        }
        
        if (chartType === 'records') {
            // Records count chart
            const maxRecords = Math.max(...dailyData.map(d => d.record_count));
            const minRecords = Math.min(...dailyData.map(d => d.record_count));
            const range = maxRecords - minRecords || 1;
            
            // Y-axis
            dailyCtx.fillStyle = '#888';
            dailyCtx.font = '12px sans-serif';
            dailyCtx.textAlign = 'right';
            for (let i = 0; i <= 5; i++) {
                const value = minRecords + (range * (5 - i) / 5);
                const y = dailyCtx.height - 40 - (i * 30);
                dailyCtx.fillText(value.toFixed(0), dailyCtx.width - 10, y + 15);
                
                if (i > 0) {
                    dailyCtx.strokeStyle = '#ddd';
                    dailyCtx.beginPath();
                    dailyCtx.moveTo(dailyCtx.width - 40, y + 15);
                    dailyCtx.lineTo(dailyCtx.width - 10, y);
                    dailyCtx.stroke();
                }
            }
            
            // X-axis
            dailyCtx.textAlign = 'center';
            dailyData.forEach((data, index) => {
                const x = 30 + (index * (dailyCtx.width - 60) / (dailyData.length - 1));
                const y = dailyCtx.height - 30;
                dailyCtx.fillText(x, y + 15);
                
                // Records bar
                const barWidth = 20;
                const barHeight = Math.max(2, (data.record_count / range) * 100);
                const barY = dailyCtx.height - 30 - barHeight;
                
                dailyCtx.fillStyle = '#888';
                dailyCtx.fillRect(x - barWidth/2, barY, barWidth, barHeight);
                
                // Date label
                const date = new Date(data.date);
                dailyCtx.fillStyle = '#ddd';
                dailyCtx.textAlign = 'center';
                dailyCtx.fillText(date.toLocaleDateString(), x + 10, y - 5);
            });
        }
        
        // Title
        dailyCtx.fillStyle = '#333';
        dailyCtx.font = 'bold 16px sans-serif';
        dailyCtx.textAlign = 'center';
        dailyCtx.fillText(`Daily ${chartType.charAt(0).toUpperCase()}`, dailyCtx.width / 2, 30);
    }
}

async function setupDailyUsage() {
    const dailyLimitSelect = document.getElementById('daily-limit');
    const refreshButton = document.getElementById('refresh-daily');
    
    if (dailyLimitSelect) {
        dailyLimitSelect.addEventListener('change', () => {
            fetchDailyUsage();
        });
    }
    
    if (refreshButton) {
        refreshButton.addEventListener('click', fetchDailyUsage);
    }
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

function updateChartZoom(delta) {
    const newZoom = chartZoomLevel + delta;
    chartZoomLevel = Math.max(1.0, Math.min(newZoom, 10.0));
    const providerId = document.getElementById('chart-provider-filter').value;
    if (providerId) {
        renderProviderChart(providerId);
    }
}

function resetChartZoom() {
    chartZoomLevel = 1.0;
    chartPanOffset = 0;
    isDragging = false;
    dragStartX = 0;
    dragStartPan = 0;
    const providerId = document.getElementById('chart-provider-filter').value;
    if (providerId) {
        renderProviderChart(providerId);
    }
}

function calculateVisibleRange(dataLength) {
    if (chartZoomLevel <= 1.0) {
        return { startIndex: 0, endIndex: dataLength - 1 };
    }

    const visibleCount = Math.ceil(dataLength / chartZoomLevel);
    const maxPanOffset = Math.max(0, dataLength - visibleCount);
    const normalizedPan = Math.max(0, Math.min(chartPanOffset, maxPanOffset));
    
    const startIndex = Math.floor(normalizedPan);
    const endIndex = Math.min(startIndex + visibleCount - 1, dataLength - 1);
    
    return { startIndex, endIndex };
}

document.addEventListener('DOMContentLoaded', initializeDashboard);
document.addEventListener('visibilitychange', () => {
    if (document.hidden) {
        stopAutoRefresh();
    } else {
        startAutoRefresh();
    }
});
