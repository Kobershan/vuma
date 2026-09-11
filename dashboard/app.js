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
      const response = await fetch('/api/v1/operator/', { credentials: 'same-origin', headers: { Accept: 'application/json' } });
      if (response.ok) document.querySelector('#last-updated').textContent = `Updated ${new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`;
    } finally {
      button.disabled = false;
      button.textContent = 'Refresh data';
    }
  });
})();
