using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace VumaRetail.Desktop;

public partial class MainWindow : Window
{
    private readonly DesktopApi _api = new();
    private static readonly Dictionary<string, (string Title, string Description, string Capabilities)> Modules = new()
    {
        ["POS"] = ("Point of sale", "Run a complete till session with scanned lines, tenders, receipts, returns and cash-up.", "Till sessions • basket lines • tender capture • mixed-company allocation • receipts • returns"),
        ["Inventory"] = ("Inventory & catalogue", "Control products, variants, barcodes, availability, sourcing and warehouse movement.", "Items and variants • barcodes • availability • stock transfers • cycle counts • bins • putaway • picking"),
        ["Orders"] = ("Orders", "Track the full order lifecycle from creation through fulfilment, settlement, cancellation and returns.", "Create and edit orders • allocate stock • confirm • release for dispatch • complete • returns"),
        ["Procurement"] = ("Procurement", "Manage suppliers, requisitions, RFQs, purchase orders, receipts and three-way matching.", "Requisitions • RFQs • supplier responses • approvals • purchase orders • goods receipts • invoice matching"),
        ["Logistics"] = ("Logistics", "Plan delivery runs and record carrier dispatch, tracking and proof of delivery.", "Carriers • delivery runs • stops • dispatch • tracking • proof of delivery"),
        ["Finance"] = ("Finance", "Operate the accounting layer with journals, invoices, payments, tax, reconciliation and reporting.", "Chart of accounts • journals • AR/AP • bank reconciliation • tax • trial balance • financial statements"),
        ["CRM"] = ("Customers & CRM", "Build a complete customer view across leads, opportunities, activities, consent and accounts.", "Leads • opportunities • activities • segments • consent • customer 360 • accounts and ageing"),
        ["Manufacturing"] = ("Manufacturing", "Define and publish bills of materials and connect production planning to inventory.", "Bills of materials • versions • publish • material availability"),
        ["Loyalty"] = ("Loyalty", "Manage customer enrolment, earning, redemption, tiers and rewards through the loyalty API.", "Members • balances • earn • redeem • tiers • rewards • catalogue sync"),
        ["Administration"] = ("Administration", "Control users, roles, permissions, companies, stores, terminals and tenant access.", "Users • roles • permissions • companies • stores • terminals • company access"),
        ["Sync"] = ("Sync & backups", "Monitor store/cloud sync, conflicts, snapshots and verification status.", "Batch status • conflicts • conflict resolution • snapshots • snapshot verification"),
    };

    public MainWindow()
    {
        InitializeComponent();
        var configured = Environment.GetEnvironmentVariable("VUMA_API_BASE_URL");
        if (!string.IsNullOrWhiteSpace(configured)) _api.BaseUrl = configured.TrimEnd('/');
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        LoginError.Text = string.Empty;
        try
        {
            await _api.SignInAsync(UsernameInput.Text.Trim(), PasswordInput.Password);
            ConnectedEndpoint.Text = _api.BaseUrl;
            LoginView.Visibility = Visibility.Collapsed;
            DashboardView.Visibility = Visibility.Visible;
            await RefreshOverviewAsync();
        }
        catch (Exception ex)
        {
            LoginError.Text = ex.Message;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshOverviewAsync();

    private async Task RefreshOverviewAsync()
    {
        try
        {
            var data = await _api.GetAsync<DashboardOverview>("/dashboard/overview");
            SalesToday.Text = $"{data.SalesToday:N2} {data.Currency}";
            OrdersToday.Text = data.OrdersToday.ToString("N0");
            OpenOrders.Text = data.OpenOrders.ToString("N0");
            RecentActivity.Text = data.RecentOrders.Count == 0
                ? "No recent orders were returned for this tenant."
                : string.Join(Environment.NewLine, data.RecentOrders.Select(x => $"{x.OrderNumber}  ·  {x.Status}  ·  {x.Gross:N2} {x.Currency}"));
            StatusText.Text = $"Last refreshed {DateTime.Now:t}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not refresh live data: {ex.Message}";
        }
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;
        if (key == "Overview")
        {
            PageEyebrow.Text = "OPERATIONS";
            PageTitle.Text = "Good morning";
            PageSubtitle.Text = "Everything important, at a glance.";
            OverviewPanel.Visibility = Visibility.Visible;
            ActivityPanel.Visibility = Visibility.Visible;
            ModulePanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;
            _ = RefreshOverviewAsync();
            return;
        }

        if (key == "Settings")
        {
            PageEyebrow.Text = "CONTROL";
            PageTitle.Text = "Settings";
            PageSubtitle.Text = "Configure this client after authentication.";
            OverviewPanel.Visibility = Visibility.Collapsed;
            ActivityPanel.Visibility = Visibility.Collapsed;
            ModulePanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Visible;
            SettingsApiUrl.Text = _api.BaseUrl;
            StatusText.Text = "Connection settings are available only after sign-in.";
            return;
        }

        if (!Modules.TryGetValue(key, out var module)) return;
        PageEyebrow.Text = "VUMA RETAIL OS";
        PageTitle.Text = module.Title;
        PageSubtitle.Text = module.Description;
        OverviewPanel.Visibility = Visibility.Collapsed;
        ActivityPanel.Visibility = Visibility.Collapsed;
        ModulePanel.Visibility = Visibility.Visible;
        SettingsPanel.Visibility = Visibility.Collapsed;
        ModuleTitle.Text = module.Title;
        ModuleDescription.Text = module.Description;
        ModuleCapabilities.Text = module.Capabilities;
        StatusText.Text = "Connected to the authenticated API. Actions are permission-scoped to the signed-in tenant.";
    }

    private void NewSale_Click(object sender, RoutedEventArgs e)
    {
        Navigate_Click(new Button { Tag = "POS" }, e);
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var value = SettingsApiUrl.Text.Trim().TrimEnd('/');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            StatusText.Text = "Enter a valid HTTP or HTTPS API endpoint.";
            return;
        }

        _api.BaseUrl = value;
        ConnectedEndpoint.Text = value;
        StatusText.Text = "API endpoint saved for this session. Sign in again if the endpoint changed.";
    }

    private sealed class DesktopApi
    {
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
        public string BaseUrl { get; set; } = "https://localhost:7243/api/v1";

        public async Task SignInAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(BaseUrl)) throw new InvalidOperationException("Enter an API endpoint.");
            using var response = await _http.PostAsJsonAsync($"{BaseUrl}/auth/token", new { userName = username, password });
            await EnsureSuccess(response);
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>() ?? throw new InvalidOperationException("The API returned no token.");
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        }

        public async Task<T> GetAsync<T>(string path)
        {
            using var response = await _http.GetAsync($"{BaseUrl}{path}");
            await EnsureSuccess(response);
            return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidOperationException("The API returned no data.");
        }

        private static async Task EnsureSuccess(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"API returned {(int)response.StatusCode}: {body}");
        }
    }

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken, Guid UserId, string DisplayName);
    private sealed record DashboardOverview(decimal SalesToday, int OrdersToday, int OpenOrders, IReadOnlyList<DashboardOrder> RecentOrders, DateTimeOffset AsAt)
    {
        public string Currency => RecentOrders.FirstOrDefault()?.Currency ?? "ZAR";
    }
    private sealed record DashboardOrder(string OrderNumber, string Currency, decimal Gross, string Status, DateTimeOffset OrderDate);
}
