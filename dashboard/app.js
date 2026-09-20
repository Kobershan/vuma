(() => {
  const $ = (selector) => document.querySelector(selector);
  const root = document.documentElement;
  let token;
  let demoLocations = [];
  let demoItemId = '';
  const modules = [
    { id: 'overview', label: 'Overview', icon: '⌂', description: 'Live performance across your company.' },
    { id: 'sales', label: 'Sales', icon: '↗', description: 'Orders, sales, returns and customer activity.', resources: [['Recent orders', '/orders'], ['Sales analytics', '/sales/analytics/']], actions: [['New order', 'order']] },
    { id: 'inventory', label: 'Inventory', icon: '▦', description: 'Stock locations, balances and movements.', resources: [['Stock locations', '/inventory/locations/']], actions: [['Receive stock', 'receive-stock']] },
    { id: 'warehouse', label: 'Warehouse', icon: '▤', description: 'Zones, bins, pick waves and fulfilment.', resources: [['Warehouse zones', '/warehouse/zones/']] },
    { id: 'procurement', label: 'Procurement', icon: '⇩', description: 'Purchase orders, requisitions and goods received.', resources: [['Purchase orders', '/procurement/purchase-orders/'], ['Requisitions', '/procurement/requisitions/']] },
    { id: 'finance', label: 'Finance', icon: 'R', description: 'Accounts, journals, trial balance and cash control.', resources: [['Chart of accounts', '/finance/accounts/'], ['Posted journals', '/finance/journals/'], ['Trial balance', '/finance/accounts/trial-balance']] },
    { id: 'logistics', label: 'Logistics', icon: '⌁', description: 'Shipments, carriers, vehicles and delivery runs.', resources: [['Shipments', '/logistics/shipments'], ['Carriers', '/logistics/carriers'], ['Vehicles', '/logistics/vehicles']] },
    { id: 'field-sales', label: 'Reps & Field Sales', icon: '♙', description: 'Rep pro-formas, approvals, availability and performance.', resources: [['Rep availability', '/field-sales/availability'], ['Rep performance', '/field-sales/performance']], actions: [['New pro forma', 'pro-forma']] },
    { id: 'customers', label: 'Customers', icon: '◎', description: 'Partners, accounts and customer relationships.', resources: [['Customers', '/partners/'], ['Customer accounts', '/customer-accounts/']] },
    { id: 'sync', label: 'Offline sync', icon: '↻', description: 'Terminal queues, acknowledgements and replay status.', resources: [['Sync status', '/sync/status'], ['Sync batches', '/sync/batches']] },
    { id: 'admin', label: 'Administration', icon: '⚙', description: 'Companies, users, terminals and system configuration.', resources: [['Companies', '/registry/companies/'], ['Terminals', '/registry/terminals/']] }
  ];
  const storedTheme = localStorage.getItem('vuma-theme');
  if (storedTheme === 'dark' || storedTheme === 'light') root.dataset.theme = storedTheme;
  $('#theme-toggle').addEventListener('click', () => { const next = root.dataset.theme === 'dark' ? 'light' : 'dark'; root.dataset.theme = next; localStorage.setItem('vuma-theme', next); });
  const request = (path) => { const headers = new Headers({ Accept: 'application/json' }); if (token) headers.set('Authorization', `Bearer ${token}`); return fetch(`/api/v1${path}`, { headers, credentials: 'same-origin' }); };
  const esc = (value) => String(value ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const list = (data) => Array.isArray(data) ? data : (data?.items ?? data?.data ?? (data == null ? [] : [data]));
  const title = (key) => key.replace(/([A-Z])/g, ' $1').replace(/^./, (c) => c.toUpperCase());
  const display = (key, value) => {
    if (value == null) return '—';
    if (typeof value === 'object') return Array.isArray(value) ? `${value.length} items` : 'Details';
    if (key.toLowerCase().includes('date') || key.endsWith('At') || key.endsWith('On')) { const date = new Date(value); if (!Number.isNaN(date.valueOf())) return date.toLocaleDateString('en-ZA'); }
    if (['net', 'tax', 'gross', 'amount', 'unitCost', 'salesToday'].includes(key)) return new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR' }).format(Number(value));
    return String(value);
  };
  const showLogin = () => { $('#login-panel').hidden = false; $('#app-shell').hidden = true; };
  const showApp = (name) => { $('#login-panel').hidden = true; $('#app-shell').hidden = false; $('#user-name').textContent = name || 'Signed in'; renderNavigation(); route(); };
  function renderNavigation() { $('#navigation').innerHTML = modules.map((m) => `<a class="nav-item" data-module="${m.id}" href="#${m.id}"><span class="nav-icon">${m.icon}</span><span>${m.label}</span></a>`).join(''); }
  async function get(path) { const response = await request(path); if (response.status === 401 || response.status === 403) { token = undefined; showLogin(); throw new Error('Your session has expired.'); } if (!response.ok) throw new Error(`${response.status} ${response.statusText}`); return response.json(); }
  async function resource(path, label) { try { return { label, path, data: await get(path) }; } catch (error) { return { label, path, error: error.message }; } }
  function table(data) {
    const rows = list(data);
    if (!rows.length) return '<div class="empty"><strong>No records yet</strong><br><span>When this area is used, records will appear here.</span></div>';
    const keys = [...new Set(rows.flatMap((row) => Object.keys(row || {})))].filter((key) => key !== 'id' && key !== 'lines').slice(0, 7);
    return `<table class="data-table"><thead><tr>${keys.map((key) => `<th>${esc(title(key))}</th>`).join('')}</tr></thead><tbody>${rows.slice(0, 100).map((row) => `<tr>${keys.map((key) => `<td>${esc(display(key, row?.[key]))}</td>`).join('')}</tr>`).join('')}</tbody></table>${rows.length > 100 ? '<p class="muted">Showing the first 100 records.</p>' : ''}`;
  }
  function openAction(action) {
    const modal = document.createElement('div'); modal.className = 'modal-backdrop';
    const locations = demoLocations.map((location) => `<option value="${esc(location.id)}">${esc(location.name)} (${esc(location.code)})</option>`).join('');
    let heading; let endpoint; let fields;
    if (action === 'order') {
      heading = 'New customer order'; endpoint = '/orders';
      fields = `<label>Channel<select name="channel"><option>InStore</option><option>Phone</option><option>Online</option><option>FieldSales</option></select></label><label>Fulfilment<select name="fulfilmentType"><option>Delivery</option><option>ClickAndCollect</option><option>CounterSale</option></select></label><label>Fulfilling location<select name="fulfillingLocationId">${locations}</select></label><label>Currency<select name="currency"><option>ZAR</option><option>USD</option></select></label><label class="wide">Delivery address<input name="deliveryLine1" placeholder="Optional"></label>`;
    } else if (action === 'receive-stock') {
      heading = 'Receive new stock'; endpoint = '/inventory/stock/receive';
      fields = `<label>Location<select name="locationId">${locations}</select></label><label>Item ID<input name="itemId" value="${esc(demoItemId)}" required></label><label>Quantity<input name="quantity" type="number" min="0.000001" step="0.000001" value="10" required></label><label>Unit<select name="unitOfMeasure"><option>EA</option><option>KG</option><option>L</option></select></label><label>Unit cost<input name="unitCost" type="number" min="0" step="0.0001" value="42.50" required></label><label>Currency<select name="currency"><option>ZAR</option><option>USD</option></select></label><label class="wide">Note<input name="note" value="Demo stock receipt"></label>`;
    } else {
      heading = 'New rep pro forma'; endpoint = '/field-sales/pro-formas';
      fields = `<label>Rep ID<input name="repId" required placeholder="Rep UUID"></label><label>Company ID<input name="companyId" required placeholder="Company UUID"></label><label>Customer / partner ID<input name="partnerId" required placeholder="Partner UUID"></label><label>Currency<select name="currency"><option>ZAR</option><option>USD</option></select></label><label>Item ID<input name="itemId" value="${esc(demoItemId)}" required></label><label>Quantity<input name="quantityValue" type="number" min="0.000001" step="0.000001" value="1" required></label><label>Unit price<input name="unitPriceAmount" type="number" min="0" step="0.0001" value="100" required></label><label>Tax code<input name="taxCode" value="STANDARD" required></label>`;
    }
    modal.innerHTML = `<div class="modal panel"><div class="panel-head"><div><p class="eyebrow">Demo workflow</p><h2>${heading}</h2><p class="muted">This saves through the real Vuma command endpoint.</p></div><button type="button" class="modal-x modal-close">×</button></div><form class="modal-form" data-endpoint="${endpoint}"><div class="form-grid">${fields}</div><div class="form-actions"><button type="button" class="button secondary modal-close">Cancel</button><button class="button primary" type="submit">Save</button></div><p class="modal-result muted"></p></form></div>`;
    document.body.appendChild(modal); modal.querySelectorAll('.modal-close').forEach((button) => button.addEventListener('click', () => modal.remove())); modal.querySelector('form').addEventListener('submit', (event) => submitAction(event.currentTarget, action, modal));
  }
  async function submitAction(form, action, modal) {
    const body = Object.fromEntries(new FormData(form).entries());
    for (const key of ['quantity', 'unitCost', 'quantityValue', 'unitPriceAmount']) if (body[key] !== undefined) body[key] = Number(body[key]);
    if (action === 'pro-forma') { body.idempotencyKey = crypto.randomUUID(); body.lines = [{ itemId: body.itemId, itemVariantId: null, quantityValue: body.quantityValue, quantityUom: 'EA', unitPriceAmount: body.unitPriceAmount, discountAmount: 0, taxCode: body.taxCode, currency: body.currency }]; delete body.itemId; delete body.quantityValue; delete body.unitPriceAmount; delete body.taxCode; }
    const result = modal.querySelector('.modal-result'); result.textContent = 'Saving…';
    const response = await fetch(`/api/v1${form.dataset.endpoint}`, { method: 'POST', headers: { Accept: 'application/json', 'Content-Type': 'application/json', Authorization: `Bearer ${token}` }, body: JSON.stringify(body) });
    const text = await response.text(); result.textContent = response.ok ? 'Saved successfully.' : `Could not save (${response.status}). ${text}`; result.className = `modal-result ${response.ok ? 'success' : 'error'}`; if (response.ok) setTimeout(() => { modal.remove(); route(); }, 700);
  }
  async function renderOverview() {
    $('#page-title').textContent = 'Overview';
    $('#workspace').innerHTML = `<div class="page-intro"><div><p class="eyebrow">Live operational control</p><h2>Good morning — here is your business today</h2><p class="muted">Every number below is read from your Vuma company database.</p></div><button id="refresh" class="button">Refresh</button></div><div id="overview-stats" class="stats-grid"></div><div class="panel"><div class="panel-head"><div><h3>Recent orders</h3><span class="muted">Latest activity</span></div><a class="button secondary" href="#sales">View sales</a></div><div id="recent-orders"></div></div><div class="workspace-grid">${modules.slice(1).map((m) => `<a class="module-card" href="#${m.id}"><h3>${m.icon} ${m.label}</h3><p>${m.description}</p><span>Open workspace →</span></a>`).join('')}</div>`;
    $('#refresh').addEventListener('click', route);
    const [overview, locations, accounts] = await Promise.all([resource('/dashboard/overview', ''), resource('/inventory/locations/', ''), resource('/finance/accounts/', '')]);
    const o = overview.data || {};
    $('#overview-stats').innerHTML = [['Sales today', display('salesToday', o.salesToday || 0)], ['Orders today', o.ordersToday || 0], ['Inventory locations', list(locations.data).length], ['Finance accounts', list(accounts.data).length]].map(([label, value]) => `<article class="stat-card"><span class="stat-label">${label}</span><strong>${esc(value)}</strong><span class="stat-meta">Live from Vuma</span></article>`).join('');
    $('#recent-orders').innerHTML = table(o.recentOrders || []);
  }
  async function renderModule(module) {
    $('#page-title').textContent = module.label;
    if (module.actions?.length && !demoLocations.length) { const locations = await resource('/inventory/locations/', ''); demoLocations = list(locations.data); }
    $('#workspace').innerHTML = `<div class="page-intro"><div><p class="eyebrow">ERP workspace</p><h2>${module.label}</h2><p class="muted">${module.description}</p></div><div class="page-actions"><span class="live-badge"><i></i> Live data</span>${(module.actions || []).map(([label, action]) => `<button class="button primary action-button" data-action="${action}">${label}</button>`).join('')}</div></div><div id="resource-grid"></div>`;
    document.querySelectorAll('.action-button').forEach((button) => button.addEventListener('click', () => openAction(button.dataset.action)));
    const grid = $('#resource-grid');
    for (const [label, path] of module.resources || []) {
      const result = await resource(path, label);
      const panel = document.createElement('article'); panel.className = 'panel';
      panel.innerHTML = `<div class="panel-head"><div><h3>${esc(label)}</h3><span class="muted">Current records in your company</span></div><button class="button secondary reload">Refresh</button></div>${result.error ? `<p class="error">This area could not be loaded: ${esc(result.error)}</p>` : table(result.data)}`;
      panel.querySelector('.reload').addEventListener('click', route); grid.appendChild(panel);
    }
    if (module.id === 'inventory') await renderInventoryBalances(grid);
  }
  async function renderInventoryBalances(grid) {
    const locations = await resource('/inventory/locations/', ''); demoLocations = list(locations.data);
    for (const location of list(locations.data)) {
      const result = await resource(`/inventory/locations/${location.id}/balances`, location.name);
      const panel = document.createElement('article'); panel.className = 'panel'; panel.innerHTML = `<div class="panel-head"><div><h3>${esc(location.name)}</h3><span class="muted">${esc(location.code)} · Stock balances</span></div></div>${result.error ? `<p class="error">${esc(result.error)}</p>` : table(result.data)}`; grid.appendChild(panel);
      const ledger = await resource(`/inventory/locations/${location.id}/ledger`, location.name);
      const firstMovement = list(ledger.data)[0]; if (!demoItemId && firstMovement) demoItemId = firstMovement.itemId || firstMovement.itemVariantId || '';
      const ledgerPanel = document.createElement('article'); ledgerPanel.className = 'panel'; ledgerPanel.innerHTML = `<div class="panel-head"><div><h3>${esc(location.name)} movement ledger</h3><span class="muted">Receipts, issues, returns and adjustments</span></div></div>${ledger.error ? `<p class="error">${esc(ledger.error)}</p>` : table(ledger.data)}`; grid.appendChild(ledgerPanel);
    }
  }
  async function route() { const id = location.hash.slice(1) || 'overview'; document.querySelectorAll('.nav-item').forEach((a) => a.classList.toggle('active', a.dataset.module === id)); const module = modules.find((m) => m.id === id) || modules[0]; try { if (module.id === 'overview') await renderOverview(); else await renderModule(module); $('#last-updated').textContent = `Updated ${new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`; } catch (error) { $('#workspace').innerHTML = `<div class="panel"><p class="error">${esc(error.message)}</p></div>`; } }
  $('#login-form').addEventListener('submit', async (event) => { event.preventDefault(); const form = new FormData(event.currentTarget); const error = $('#login-error'); error.textContent = ''; try { const response = await fetch('/api/v1/auth/token', { method: 'POST', headers: { 'Content-Type': 'application/json', Accept: 'application/json' }, body: JSON.stringify({ userName: form.get('userName'), password: form.get('password') }) }); if (response.status === 429) throw new Error('Too many sign-in attempts. Wait one minute, then try again.'); if (!response.ok) throw new Error('Sign-in failed. Check the username and password.'); const session = await response.json(); token = session.accessToken; showApp(session.displayName); } catch (e) { error.textContent = e.message; } });
  $('#sign-out').addEventListener('click', () => { token = undefined; showLogin(); }); window.addEventListener('hashchange', route); showLogin();
})();
