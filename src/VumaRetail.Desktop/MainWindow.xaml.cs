using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VumaRetail.Application.Abstractions;
using VumaRetail.Contracts.Identity;
using VumaRetail.Contracts.Pos;
using VumaRetail.Desktop.Offline;

namespace VumaRetail.Desktop;

/// <summary>The terminal-bound PIN entry surface. Business operations remain API calls.</summary>
public partial class MainWindow : Window
{
    private readonly TerminalApi _api;
    private readonly TerminalSyncOutbox _outbox;
    private string _pin = string.Empty;
    private Guid _tillSessionId;
    private Guid _saleId;
    private IReadOnlyCollection<string> _permissions = Array.Empty<string>();
    private readonly System.Windows.Threading.DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow(IClock clock)
    {
        _ = clock;
        _api = new TerminalApi();
        string outboxPath = Environment.GetEnvironmentVariable("VUMA_TERMINAL_OUTBOX_PATH")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vuma", "terminal-outbox.db");
        _outbox = new TerminalSyncOutbox(outboxPath);
        _ = _outbox.InitializeAsync();
        InitializeComponent();
        StoreNameText.Text = Environment.GetEnvironmentVariable("VUMA_STORE_NAME") ?? "Vuma store";
        TerminalText.Text = $"Terminal {(_api.TerminalId == Guid.Empty ? "not configured" : _api.TerminalId)}";
        ConnectionText.Text = $"API {_api.BaseUrl}";
        _clockTimer.Tick += async (_, _) =>
        {
            StatusClock.Text = DateTimeOffset.Now.ToString("HH:mm:ss");
            try { StatusSync.Text = $"Sync queue {await _outbox.CountOutstandingAsync()}"; }
            catch { StatusSync.Text = "Sync queue unavailable"; }
        };
        _clockTimer.Start();
    }

    private void PinKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string digit } && _pin.Length < 8)
        {
            _pin += digit;
            PinDisplay.Text = new string('•', _pin.Length);
            LoginError.Text = string.Empty;
        }
    }

    private void ClearPin_Click(object sender, RoutedEventArgs e) => ClearPin();

    private void BackspacePin_Click(object sender, RoutedEventArgs e)
    {
        if (_pin.Length == 0) return;
        _pin = _pin[..^1];
        PinDisplay.Text = new string('•', _pin.Length);
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        LoginError.Text = string.Empty;
        try
        {
            if (_pin.Length is < 4 or > 8) throw new InvalidOperationException("Enter a 4–8 digit PIN.");
            if (_api.TerminalId == Guid.Empty) throw new InvalidOperationException("This terminal is not configured.");

            TokenResponse token = await _api.SignInWithPinAsync(_pin);
            _permissions = (await _api.GetPermissionsAsync()).Permissions;
            ConnectionText.Text = $"Online · {token.DisplayName}";
            PinLoginView.Visibility = Visibility.Collapsed;
            TillView.Visibility = Visibility.Visible;
            StatusOperator.Text = token.DisplayName;
            StatusConnection.Text = "Online";
            UnitPriceInput.IsEnabled = _permissions.Contains("pos.price.override", StringComparer.OrdinalIgnoreCase);
            await StartTillAsync();
        }
        catch (Exception ex)
        {
            ClearPin();
            LoginError.Text = ex.Message;
            ConnectionText.Text = "Sign-in failed · check the terminal connection";
        }
    }

    private void ClearPin()
    {
        _pin = string.Empty;
        PinDisplay.Text = string.Empty;
    }

    private async Task StartTillAsync()
    {
        try
        {
            _tillSessionId = await _api.OpenTillSessionAsync(
                decimal.TryParse(Environment.GetEnvironmentVariable("VUMA_OPENING_FLOAT"), out decimal openingFloat)
                    ? openingFloat
                    : 0m,
                Environment.GetEnvironmentVariable("VUMA_CURRENCY") ?? "ZAR");
            await StartNewSaleAsync();
        }
        catch (Exception ex)
        {
            TillError.Text = ex.Message;
            StatusConnection.Text = "Online · till not open";
        }
    }

    private async Task StartNewSaleAsync()
    {
        Guid locationId = ReadRequiredGuid("VUMA_LOCATION_ID", "A stock location is not configured.");
        _saleId = await _api.OpenSaleAsync(locationId);
        await RefreshSaleAsync();
    }

    private async void SearchInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await AddLineAsync();
    }

    private async void AddLine_Click(object sender, RoutedEventArgs e) => await AddLineAsync();

    private async Task AddLineAsync()
    {
        try
        {
            if (_saleId == Guid.Empty) throw new InvalidOperationException("Open a sale before adding a line.");
            SellableItemResponse item = await _api.LookupBarcodeAsync(SearchInput.Text.Trim());
            decimal quantity = decimal.Parse(QuantityInput.Text, System.Globalization.CultureInfo.InvariantCulture);
            decimal unitPrice = decimal.Parse(UnitPriceInput.Text, System.Globalization.CultureInfo.InvariantCulture);
            await _api.AddLineAsync(_saleId, item, quantity, unitPrice, Environment.GetEnvironmentVariable("VUMA_CURRENCY") ?? "ZAR");
            SearchInput.Clear();
            QuantityInput.Text = "1";
            await RefreshSaleAsync();
        }
        catch (Exception ex) { TillError.Text = ex.Message; }
    }

    private async Task RefreshSaleAsync()
    {
        SaleResponse sale = await _api.GetSaleAsync(_saleId);
        SaleNumberText.Text = sale.SaleNumber;
        NetText.Text = $"{sale.Net:N2} {sale.Currency}";
        TaxText.Text = $"{sale.Tax:N2} {sale.Currency}";
        GrossText.Text = $"{sale.Gross:N2} {sale.Currency}";
        LinesList.ItemsSource = sale.Lines.Where(line => !line.IsVoided).ToList();
        decimal remaining = Math.Max(0m, sale.Gross - sale.AmountTendered);
        decimal change = Math.Max(0m, sale.AmountTendered - sale.Gross);
        TenderDueText.Text = $"Remaining {remaining:N2} {sale.Currency}";
        ChangeDueText.Text = $"Change due {change:N2} {sale.Currency}";
    }

    private async void NewSale_Click(object sender, RoutedEventArgs e)
    {
        try { await StartNewSaleAsync(); TillError.Text = string.Empty; }
        catch (Exception ex) { TillError.Text = ex.Message; }
    }

    private async void ParkSale_Click(object sender, RoutedEventArgs e)
    {
        try { await _api.PostNoContentAsync($"/pos/sales/{_saleId}/park"); await StartNewSaleAsync(); }
        catch (Exception ex) { TillError.Text = ex.Message; }
    }

    private async void Parked_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ParkedSalesList.ItemsSource = await _api.ListParkedSalesAsync();
            ParkedView.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { TillError.Text = ex.Message; }
    }

    private void ParkedClose_Click(object sender, RoutedEventArgs e) => ParkedView.Visibility = Visibility.Collapsed;

    private async void ResumeParked_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ParkedSalesList.SelectedItem is not SaleResponse sale)
                throw new InvalidOperationException("Select a parked sale to resume.");
            await _api.PostNoContentAsync($"/pos/sales/{sale.Id}/resume");
            _saleId = sale.Id;
            ParkedView.Visibility = Visibility.Collapsed;
            await RefreshSaleAsync();
        }
        catch (Exception ex) { TillError.Text = ex.Message; }
    }

    private async void VoidLine_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (LinesList.SelectedItem is not SaleLineResponse line)
                throw new InvalidOperationException("Select a line to void.");
            await _api.PostNoContentAsync($"/pos/sales/{_saleId}/lines/{line.Id}/void");
            await RefreshSaleAsync();
        }
        catch (Exception ex) { TillError.Text = ex.Message; }
    }

    private async void Tender_Click(object sender, RoutedEventArgs e)
    {
        TenderError.Text = string.Empty;
        TenderView.Visibility = Visibility.Visible;
        await RefreshSaleAsync();
    }

    private void TenderBack_Click(object sender, RoutedEventArgs e) => TenderView.Visibility = Visibility.Collapsed;

    private async void AddTender_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string type = (TenderTypeInput.SelectedItem as ComboBoxItem)?.Content?.ToString()
                ?? throw new InvalidOperationException("Select a tender type.");
            decimal amount = decimal.Parse(TenderAmountInput.Text, System.Globalization.CultureInfo.InvariantCulture);
            string currency = Environment.GetEnvironmentVariable("VUMA_CURRENCY") ?? "ZAR";
            await _api.AddTenderAsync(_saleId, type, amount, currency);
            TenderAmountInput.Text = "0";
            await RefreshSaleAsync();
        }
        catch (Exception ex) { TenderError.Text = ex.Message; }
    }

    private async void CompleteSale_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaleCompletionResponse result = await _api.CompleteSaleAsync(_saleId);
            TenderError.Text = result.StockIssuesRefused == 0
                ? $"Completed {result.SaleNumber}. Change {result.ChangeGiven:N2} {result.Currency}."
                : $"Completed with {result.StockIssuesRefused} stock reconciliation issue(s).";
            TenderView.Visibility = Visibility.Collapsed;
            await ShowReceiptAsync();
            StatusConnection.Text = "Online · sale complete";
        }
        catch (Exception ex) { TenderError.Text = ex.Message; }
    }

    private async Task ShowReceiptAsync()
    {
        ReceiptResponse receipt = await _api.GetReceiptAsync(_saleId);
        ReceiptText.Text = receipt.PlainText;
        ReceiptView.Visibility = Visibility.Visible;
    }

    private void ReceiptClose_Click(object sender, RoutedEventArgs e) => ReceiptView.Visibility = Visibility.Collapsed;

    private async void RecordPrint_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _api.RecordReceiptPrintAsync(_saleId);
            ReceiptView.Visibility = Visibility.Collapsed;
            await StartNewSaleAsync();
            StatusConnection.Text = "Online · ready for next sale";
        }
        catch (Exception ex) { ReceiptText.Text = ex.Message; }
    }

    private async void CashUp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            TillSessionResponse session = await _api.GetTillSessionAsync(_tillSessionId);
            ExpectedCashText.Text = $"Expected {session.ExpectedCash:N2} {session.Currency}";
            CountedCashInput.Text = session.ExpectedCash.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            CashVarianceText.Text = "Variance is calculated on close.";
            CashUpError.Text = string.Empty;
            CashUpView.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { CashUpError.Text = ex.Message; }
    }

    private void CashUpBack_Click(object sender, RoutedEventArgs e) => CashUpView.Visibility = Visibility.Collapsed;

    private async void CloseCashUp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            decimal counted = decimal.Parse(CountedCashInput.Text, System.Globalization.CultureInfo.InvariantCulture);
            string currency = Environment.GetEnvironmentVariable("VUMA_CURRENCY") ?? "ZAR";
            CashUpResponse result = await _api.CloseTillSessionAsync(_tillSessionId, counted, currency);
            CashVarianceText.Text = $"Variance {result.Variance:N2} {result.Currency}";
            CashUpError.Text = "Shift closed. Sign in again to open the next drawer.";
            StatusConnection.Text = "Online · shift closed";
        }
        catch (Exception ex) { CashUpError.Text = ex.Message; }
    }

    private static Guid ReadRequiredGuid(string variable, string message)
        => Guid.TryParse(Environment.GetEnvironmentVariable(variable), out Guid value)
            ? value
            : throw new InvalidOperationException(message);

    private async void Window_Closed(object? sender, EventArgs e)
    {
        _clockTimer.Stop();
        await _outbox.DisposeAsync();
    }

    private sealed class TerminalApi
    {
        private readonly HttpClient _http;
        public string BaseUrl { get; } = (Environment.GetEnvironmentVariable("VUMA_API_BASE_URL") ?? "https://localhost:7243/api/v1").TrimEnd('/');
        public Guid TerminalId { get; } = Guid.TryParse(Environment.GetEnvironmentVariable("VUMA_TERMINAL_ID"), out Guid id) ? id : Guid.Empty;

        public TerminalApi()
        {
            var handler = new HttpClientHandler();
            string? certificatePath = Environment.GetEnvironmentVariable("VUMA_TERMINAL_PFX_PATH");
            if (!string.IsNullOrWhiteSpace(certificatePath))
            {
                string password = Environment.GetEnvironmentVariable("VUMA_TERMINAL_PFX_PASSWORD") ?? string.Empty;
                handler.ClientCertificates.Add(new X509Certificate2(certificatePath, password));
            }

            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        }

        public async Task<TokenResponse> SignInWithPinAsync(string pin)
        {
            using HttpResponseMessage response = await _http.PostAsJsonAsync(
                $"{BaseUrl}/auth/pin", new PinSignInRequest(TerminalId, pin));
            await EnsureSuccess(response);
            TokenResponse token = await response.Content.ReadFromJsonAsync<TokenResponse>()
                ?? throw new InvalidOperationException("The API returned no token.");
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            return token;
        }

        public async Task<Guid> OpenTillSessionAsync(decimal openingFloat, string currency)
        {
            PosIdResponse result = await PostAsync<OpenTillSessionRequest, PosIdResponse>(
                "/pos/till-sessions/", new OpenTillSessionRequest(openingFloat, currency));
            return result.Id;
        }

        public async Task<Guid> OpenSaleAsync(Guid locationId)
        {
            PosIdResponse result = await PostAsync<OpenSaleRequest, PosIdResponse>(
                "/pos/sales/", new OpenSaleRequest(null, locationId));
            return result.Id;
        }

        public Task<SellableItemResponse> LookupBarcodeAsync(string barcode)
            => GetAsync<SellableItemResponse>($"/pos/catalog/barcode/{Uri.EscapeDataString(barcode)}");

        public async Task AddLineAsync(Guid saleId, SellableItemResponse item, decimal quantity, decimal unitPrice, string currency)
        {
            await PostAsync<AddSaleLineRequest, PosIdResponse>(
                $"/pos/sales/{saleId}/lines",
                new AddSaleLineRequest(item.ItemId, item.ItemVariantId, quantity, item.UnitOfMeasure, unitPrice, currency));
        }

        public Task<SaleResponse> GetSaleAsync(Guid saleId) => GetAsync<SaleResponse>($"/pos/sales/{saleId}");

        public Task<IReadOnlyList<SaleResponse>> ListParkedSalesAsync()
            => GetAsync<IReadOnlyList<SaleResponse>>($"/pos/terminals/{TerminalId}/parked-sales");

        public Task<TillSessionResponse> GetTillSessionAsync(Guid sessionId)
            => GetAsync<TillSessionResponse>($"/pos/till-sessions/{sessionId}");

        public async Task<CashUpResponse> CloseTillSessionAsync(Guid sessionId, decimal countedCash, string currency)
            => await PostAsync<CloseTillSessionRequest, CashUpResponse>(
                $"/pos/till-sessions/{sessionId}/close", new CloseTillSessionRequest(countedCash, currency));

        public Task<PermissionsResponse> GetPermissionsAsync() => GetAsync<PermissionsResponse>("/me/permissions");

        public async Task AddTenderAsync(Guid saleId, string tenderType, decimal amount, string currency)
        {
            await PostAsync<TenderSaleRequest, PosIdResponse>(
                $"/pos/sales/{saleId}/tenders",
                new TenderSaleRequest(tenderType, amount, currency, null, Guid.NewGuid()));
        }

        public Task<SaleCompletionResponse> CompleteSaleAsync(Guid saleId)
            => PostAsync<object, SaleCompletionResponse>($"/pos/sales/{saleId}/complete", new { });

        public Task<ReceiptResponse> GetReceiptAsync(Guid saleId)
            => GetAsync<ReceiptResponse>($"/pos/sales/{saleId}/receipt");

        public async Task RecordReceiptPrintAsync(Guid saleId)
        {
            await PostAsync<RecordReceiptPrintRequest, PosIdResponse>(
                $"/pos/sales/{saleId}/receipt/prints", new RecordReceiptPrintRequest(null, Guid.NewGuid()));
        }

        public async Task<T> PostAsync<TRequest, T>(string path, TRequest request)
        {
            using HttpResponseMessage response = await _http.PostAsJsonAsync($"{BaseUrl}{path}", request);
            await EnsureSuccess(response);
            return await response.Content.ReadFromJsonAsync<T>()
                ?? throw new InvalidOperationException("The API returned no data.");
        }

        public async Task PostNoContentAsync(string path)
        {
            using HttpResponseMessage response = await _http.PostAsync($"{BaseUrl}{path}", null);
            await EnsureSuccess(response);
        }

        private async Task<T> GetAsync<T>(string path)
        {
            using HttpResponseMessage response = await _http.GetAsync($"{BaseUrl}{path}");
            await EnsureSuccess(response);
            return await response.Content.ReadFromJsonAsync<T>()
                ?? throw new InvalidOperationException("The API returned no data.");
        }

        private static async Task EnsureSuccess(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;
            string body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"API returned {(int)response.StatusCode}: {body}");
        }
    }
}
