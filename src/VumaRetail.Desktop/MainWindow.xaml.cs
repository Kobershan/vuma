using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using VumaRetail.Application.Abstractions;
using VumaRetail.Contracts.Identity;

namespace VumaRetail.Desktop;

/// <summary>The terminal-bound PIN entry surface. Business operations remain API calls.</summary>
public partial class MainWindow : Window
{
    private readonly TerminalApi _api;
    private string _pin = string.Empty;

    public MainWindow(IClock clock)
    {
        _ = clock;
        _api = new TerminalApi();
        InitializeComponent();
        StoreNameText.Text = Environment.GetEnvironmentVariable("VUMA_STORE_NAME") ?? "Vuma store";
        TerminalText.Text = $"Terminal {(_api.TerminalId == Guid.Empty ? "not configured" : _api.TerminalId)}";
        ConnectionText.Text = $"API {_api.BaseUrl}";
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
            ConnectionText.Text = $"Online · {token.DisplayName}";
            PinLoginView.Visibility = Visibility.Collapsed;
            TillView.Visibility = Visibility.Visible;
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

        private static async Task EnsureSuccess(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;
            string body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"API returned {(int)response.StatusCode}: {body}");
        }
    }
}
