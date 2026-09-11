(() => {
  const root = document.documentElement;
  const themeButton = document.querySelector('#theme-toggle');
  const stored = localStorage.getItem('vuma-theme');
  if (stored === 'dark' || stored === 'light') root.dataset.theme = stored;
  themeButton?.addEventListener('click', () => {
    const theme = root.dataset.theme === 'dark' ? 'light' : 'dark';
    root.dataset.theme = theme;
    localStorage.setItem('vuma-theme', theme);
  });
  document.querySelector('#refresh')?.addEventListener('click', async (event) => {
    const button = event.currentTarget;
    button.disabled = true;
    button.textContent = 'Refreshing…';
    try {
      const response = await fetch('/api/v1/dashboard/overview', { credentials: 'same-origin', headers: { Accept: 'application/json' } });
      if (response.ok) {
        const overview = await response.json();
        const currency = overview.recentOrders?.[0]?.currency ?? 'ZAR';
        document.querySelector('#sales-total').textContent = new Intl.NumberFormat('en-ZA', { style: 'currency', currency, maximumFractionDigits: 0 }).format(overview.salesToday ?? 0);
        document.querySelector('#orders-total').textContent = overview.ordersToday ?? 0;
        document.querySelector('#last-updated').textContent = `Updated ${new Date(overview.asAt ?? Date.now()).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`;
      }
    } finally {
      button.disabled = false;
      button.textContent = 'Refresh data';
    }
  });
})();
