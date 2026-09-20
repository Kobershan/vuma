(() => {
  const root = document.documentElement;
  const themeButton = document.querySelector('#theme-toggle');
  const storedTheme = localStorage.getItem('vuma-theme');
  let accessToken;

  if (storedTheme === 'dark' || storedTheme === 'light') root.dataset.theme = storedTheme;
  themeButton?.addEventListener('click', () => {
    const theme = root.dataset.theme === 'dark' ? 'light' : 'dark';
    root.dataset.theme = theme;
    localStorage.setItem('vuma-theme', theme);
  });

  const loginPanel = document.querySelector('#login-panel');
  const dashboard = document.querySelector('#dashboard-content');
  const loginForm = document.querySelector('#login-form');
  const loginError = document.querySelector('#login-error');

  const request = (path, options = {}) => {
    const headers = new Headers(options.headers ?? {});
    headers.set('Accept', 'application/json');
    if (options.body) headers.set('Content-Type', 'application/json');
    if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`);
    return fetch(`/api/v1${path}`, { ...options, headers, credentials: 'same-origin' });
  };

  const showSignedOut = () => {
    if (loginPanel) loginPanel.hidden = false;
    if (dashboard) dashboard.hidden = true;
  };

  const showSignedIn = (displayName) => {
    if (loginPanel) loginPanel.hidden = true;
    if (dashboard) dashboard.hidden = false;
    const user = document.querySelector('#user-name');
    if (user) user.textContent = displayName || 'Signed in';
  };

  const renderOverview = (overview) => {
    const currency = overview.salesByCurrency?.[0]?.currency ?? overview.recentOrders?.[0]?.currency ?? 'ZAR';
    document.querySelector('#sales-total').textContent = new Intl.NumberFormat('en-ZA', { style: 'currency', currency, maximumFractionDigits: 0 }).format(overview.salesToday ?? 0);
    document.querySelector('#orders-total').textContent = overview.ordersToday ?? 0;
    document.querySelector('#open-orders-total').textContent = overview.openOrders ?? 0;
    document.querySelector('#last-updated').textContent = `Updated ${new Date(overview.asAt ?? Date.now()).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`;
    const rows = document.querySelector('#recent-orders');
    if (!rows) return;
    rows.replaceChildren(...(overview.recentOrders ?? []).map((order) => {
      const row = document.createElement('tr');
      row.innerHTML = '<td class="mono"></td><td>—</td><td><span class="status processing"></span></td><td class="numeric"></td>';
      row.children[0].textContent = order.orderNumber;
      row.children[2].firstElementChild.textContent = order.status;
      row.children[3].textContent = new Intl.NumberFormat('en-ZA', { style: 'currency', currency: order.currency ?? currency }).format(order.gross ?? 0);
      return row;
    }));
  };

  const loadModuleSummary = async () => {
    const [locationsResponse, accountsResponse] = await Promise.all([
      request('/inventory/locations/'),
      request('/finance/accounts/')
    ]);
    if (locationsResponse.ok) {
      const locations = await locationsResponse.json();
      document.querySelector('#inventory-total').textContent = locations.length;
      document.querySelector('#inventory-meta').textContent = 'Stock locations available';
    }
    if (accountsResponse.ok) {
      const accounts = await accountsResponse.json();
      document.querySelector('#finance-total').textContent = accounts.length;
      document.querySelector('#finance-meta').textContent = 'Finance accounts configured';
    }
  };

  const loadOverview = async () => {
    const response = await request('/dashboard/overview');
    if (response.status === 401 || response.status === 403) {
      accessToken = undefined;
      showSignedOut();
      return;
    }
    if (!response.ok) throw new Error(`Dashboard request failed (${response.status})`);
    renderOverview(await response.json());
  };

  loginForm?.addEventListener('submit', async (event) => {
    event.preventDefault();
    loginError.textContent = '';
    const form = new FormData(loginForm);
    const response = await request('/auth/token', {
      method: 'POST',
      body: JSON.stringify({ userName: form.get('userName'), password: form.get('password') })
    });
    if (!response.ok) {
      loginError.textContent = 'Sign-in failed. Check the username and password.';
      return;
    }
    const session = await response.json();
    accessToken = session.accessToken;
    showSignedIn(session.displayName);
    await loadOverview();
    await loadModuleSummary();
  });

  document.querySelector('#refresh')?.addEventListener('click', async (event) => {
    const button = event.currentTarget;
    button.disabled = true;
    button.textContent = 'Refreshing…';
    try { await loadOverview(); await loadModuleSummary(); } finally {
      button.disabled = false;
      button.textContent = 'Refresh data';
    }
  });

  showSignedOut();
})();
