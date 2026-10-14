using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Microsoft.Identity.Client;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows.Interop;
using Velopack;
using Velopack.Sources;

namespace TafelEmailAssistent;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private static readonly HttpClient Http = new();
    private static readonly string[] MailGraphScopes = ["https://graph.microsoft.com/Mail.ReadWrite"];
    private static readonly string[] CalendarGraphScopes = ["https://graph.microsoft.com/Calendar.ReadWrite"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly UpdateManager _updateManager = new(
        new GithubSource("https://github.com/Rakoonir/tafel-email-assistent", null, false));
    private IPublicClientApplication? _identity;
    private string? _dataFilePath;
    private string? _preferencesFilePath;
    private UserPreferences _preferences = new();
    private bool _startupUpdateChecked;
    private bool _updateCheckInProgress;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadPreferences();
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TafelEmailAssistent",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);
            Browser.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = userDataFolder
            };

            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.WebMessageReceived += Browser_WebMessageReceived;
            Browser.CoreWebView2.NavigationCompleted += async (_, _) =>
            {
                SendSettingsInfo();
                if (_startupUpdateChecked)
                    return;

                _startupUpdateChecked = true;
                await CheckForUpdatesAsync(false);
            };
            Browser.CoreWebView2.NavigationStarting += (_, args) => OpenExternalLink(args.Uri, () => args.Cancel = true);
            Browser.CoreWebView2.NewWindowRequested += (_, args) => OpenExternalLink(args.Uri, () => args.Handled = true);
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;

            _dataFilePath = Path.Combine(_preferences.DataDirectory, "tafel-daten.json");
            if (File.Exists(_dataFilePath))
            {
                using var savedData = JsonDocument.Parse(await File.ReadAllTextAsync(_dataFilePath));
                var json = JsonSerializer.Serialize(savedData.RootElement);
                await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    $"window.__TAFEL_APP_DATA__ = {json};");
            }

            Browser.Source = new Uri(Path.Combine(AppContext.BaseDirectory, "index.html"));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Die App-Oberfläche konnte nicht gestartet werden. Ist die Microsoft Edge WebView2-Laufzeit installiert?\n\n{ex.Message}",
                "Tafel E-Mail-Assistent",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void Browser_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            var root = message.RootElement;
            var type = root.GetProperty("type").GetString();
            if (type == "checkForUpdates")
            {
                await CheckForUpdatesAsync(true);
                return;
            }

            if (type == "chooseDataFolder")
            {
                ChooseDataFolder();
                return;
            }

            if (type == "saveSettings")
            {
                try
                {
                    SaveSettings(root);
                    PostSettingsStatus(true, "Einstellungen gespeichert.");
                }
                catch (Exception ex)
                {
                    PostSettingsStatus(false, ex.Message);
                }
                return;
            }

            if (type == "saveData")
            {
                SavePersistentData(root.GetProperty("data"));
                return;
            }

            if (type == "createCalendarEvent")
            {
                var eventIndex = root.GetProperty("index").GetInt32();
                var eventSubject = root.GetProperty("subject").GetString() ?? "";
                var description = root.GetProperty("description").GetString() ?? "";
                var start = DateTime.ParseExact(
                    root.GetProperty("start").GetString() ?? "",
                    "yyyy-MM-dd'T'HH:mm:ss",
                    CultureInfo.InvariantCulture);
                var end = DateTime.ParseExact(
                    root.GetProperty("end").GetString() ?? "",
                    "yyyy-MM-dd'T'HH:mm:ss",
                    CultureInfo.InvariantCulture);
                if (end <= start)
                    throw new InvalidOperationException("Die Endzeit muss nach der Anfangszeit liegen.");

                await CreateCalendarEventAsync(eventSubject, description, start, end);
                PostCalendarResult(eventIndex, true, "Termin wurde im Outlook-Kalender eingetragen.");
                return;
            }

            if (type != "createDraft")
                return;

            var index = root.GetProperty("index").GetInt32();
            var recipient = root.GetProperty("to").GetString() ?? "";
            var subject = root.GetProperty("subject").GetString() ?? "";
            var html = root.GetProperty("html").GetString() ?? "";
            var webLink = await CreateDraftAsync(recipient, subject, html);
            Process.Start(new ProcessStartInfo(webLink) { UseShellExecute = true });
            PostResult(index, true, "Der formatierte Entwurf wurde erstellt und in Outlook im Browser geöffnet.");
        }
        catch (Exception ex)
        {
            var index = -1;
            var operation = "";
            try
            {
                using var message = JsonDocument.Parse(e.WebMessageAsJson);
                var root = message.RootElement;
                index = root.GetProperty("index").GetInt32();
                operation = root.GetProperty("type").GetString() ?? "";
            }
            catch (JsonException)
            {
            }

            if (index >= 0)
            {
                if (operation == "createCalendarEvent")
                    PostCalendarResult(index, false, ex.Message);
                else
                    PostResult(index, false, ex.Message);
            }
            else
                MessageBox.Show(ex.Message, "Outlook-Aktion fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SavePersistentData(JsonElement data)
    {
        if (_dataFilePath is null)
            return;

        var temporaryPath = _dataFilePath + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(_dataFilePath)!);
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(data, JsonOptions));
        File.Move(temporaryPath, _dataFilePath, true);
    }

    private void LoadPreferences()
    {
        var appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TafelEmailAssistent");
        Directory.CreateDirectory(appDataFolder);
        _preferencesFilePath = Path.Combine(appDataFolder, "settings.json");

        var documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _preferences.DataDirectory = Path.Combine(documentsFolder, "Tafel E-Mail-Assistent");
        _preferences.DefaultTime = "08:00";

        var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(appSettingsPath))
        {
            using var appSettings = JsonDocument.Parse(File.ReadAllText(appSettingsPath));
            if (appSettings.RootElement.TryGetProperty("Authentication", out var authentication))
                _preferences.Authentication = ReadAuthentication(authentication);
            if (appSettings.RootElement.TryGetProperty("Support", out var support))
                _preferences.Support = ReadSupport(support);
        }

        if (File.Exists(_preferencesFilePath))
        {
            var saved = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(_preferencesFilePath), JsonOptions);
            if (saved is not null)
            {
                if (!string.IsNullOrWhiteSpace(saved.DataDirectory))
                    _preferences.DataDirectory = Path.GetFullPath(saved.DataDirectory);
                if (IsQuarterHour(saved.DefaultTime))
                    _preferences.DefaultTime = saved.DefaultTime;
                if (saved.Authentication is not null
                    && Guid.TryParse(saved.Authentication.ClientId, out _)
                    && Guid.TryParse(saved.Authentication.TenantId, out _))
                    _preferences.Authentication = saved.Authentication;
                if (saved.Support is not null)
                    _preferences.Support = saved.Support;
            }
        }

        Directory.CreateDirectory(_preferences.DataDirectory);
    }

    private void SaveSettings(JsonElement root)
    {
        var defaultTime = root.GetProperty("defaultTime").GetString() ?? "08:00";
        if (!IsQuarterHour(defaultTime))
            throw new InvalidOperationException("Die Standard-Uhrzeit muss in 15-Minuten-Schritten gewählt werden.");

        var authentication = ReadAuthentication(root.GetProperty("authentication"));
        if (!Guid.TryParse(authentication.ClientId, out _)
            || !Guid.TryParse(authentication.TenantId, out _))
            throw new InvalidOperationException("Bitte eine gültige Client-ID und Tenant-ID aus Microsoft Entra eintragen.");

        if (_preferences.Authentication.ClientId != authentication.ClientId
            || _preferences.Authentication.TenantId != authentication.TenantId)
            _identity = null;

        _preferences.DefaultTime = defaultTime;
        _preferences.Authentication = authentication;
        _preferences.Support = ReadSupport(root.GetProperty("support"));
        SavePreferences();
        SendSettingsInfo();
    }

    private void ChooseDataFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Ordner für App-Daten auswählen",
            InitialDirectory = _preferences.DataDirectory,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var newDirectory = Path.GetFullPath(dialog.FolderName);
        var newDataFile = Path.Combine(newDirectory, "tafel-daten.json");
        JsonElement? existingData = null;
        if (!string.Equals(newDataFile, _dataFilePath, StringComparison.OrdinalIgnoreCase)
            && File.Exists(newDataFile))
        {
            if (MessageBox.Show(
                    "In diesem Ordner liegt bereits eine Datendatei. Sollen deren Vorlagen, Personen und Signaturen in die App geladen werden?",
                    "Vorhandene Daten laden",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            using var savedData = JsonDocument.Parse(File.ReadAllText(newDataFile));
            existingData = savedData.RootElement.Clone();
        }

        Directory.CreateDirectory(newDirectory);
        _preferences.DataDirectory = newDirectory;
        _dataFilePath = newDataFile;
        SavePreferences();
        Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "dataFolderChanged",
            dataDirectory = newDirectory,
            data = existingData
        }, JsonOptions));
    }

    private void SavePreferences()
    {
        if (_preferencesFilePath is null)
            return;

        var temporaryPath = _preferencesFilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_preferences, JsonOptions));
        File.Move(temporaryPath, _preferencesFilePath, true);
    }

    private void SendSettingsInfo()
    {
        if (Browser.CoreWebView2 is null)
            return;

        Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "settingsInfo",
            dataDirectory = _preferences.DataDirectory,
            defaultTime = _preferences.DefaultTime,
            authentication = new
            {
                clientId = _preferences.Authentication.ClientId,
                tenantId = _preferences.Authentication.TenantId
            },
            support = new
            {
                name = _preferences.Support.Name,
                email = _preferences.Support.Email,
                website = _preferences.Support.Website,
                phone = _preferences.Support.Phone,
                hours = _preferences.Support.Hours
            }
        }, JsonOptions));
    }

    private static SupportSettings ReadSupport(JsonElement section)
    {
        string Read(string key)
        {
            if (section.ValueKind != JsonValueKind.Object)
                return "";

            foreach (var property in section.EnumerateObject())
                if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
                    return property.Value.GetString() ?? "";

            return "";
        }

        return new SupportSettings
        {
            Name = Read("Name"),
            Email = Read("Email"),
            Website = Read("Website"),
            Phone = Read("Phone"),
            Hours = Read("Hours")
        };
    }

    private static AuthenticationSettings ReadAuthentication(JsonElement section)
    {
        string Read(string key)
        {
            if (section.ValueKind != JsonValueKind.Object)
                return "";

            foreach (var property in section.EnumerateObject())
                if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
                    return property.Value.GetString() ?? "";

            return "";
        }

        return new AuthenticationSettings
        {
            ClientId = Read("ClientId").Trim(),
            TenantId = Read("TenantId").Trim()
        };
    }

    private static bool IsQuarterHour(string? value) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
        && time.Minute % 15 == 0;

    private sealed class UserPreferences
    {
        public string DataDirectory { get; set; } = "";
        public string DefaultTime { get; set; } = "08:00";
        public AuthenticationSettings Authentication { get; set; } = new();
        public SupportSettings Support { get; set; } = new();
    }

    private sealed class AuthenticationSettings
    {
        public string ClientId { get; set; } = "";
        public string TenantId { get; set; } = "";
    }

    private sealed class SupportSettings
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string Website { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Hours { get; set; } = "";
    }

    private async Task<string> CreateDraftAsync(string recipient, string subject, string html)
    {
        var token = await AcquireGraphTokenAsync(MailGraphScopes);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Content = JsonContent.Create(new
        {
            subject,
            body = new { contentType = "HTML", content = html },
            toRecipients = new[] { new { emailAddress = new { address = recipient } } }
        });

        using var response = await Http.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Microsoft Graph meldet {(int)response.StatusCode}: {responseBody}");

        using var result = JsonDocument.Parse(responseBody);
        var webLink = result.RootElement.GetProperty("webLink").GetString();
        if (string.IsNullOrWhiteSpace(webLink) || !webLink.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Microsoft Graph hat keinen gültigen Outlook-Link zurückgegeben.");

        return webLink;
    }

    private async Task CreateCalendarEventAsync(string subject, string description, DateTime start, DateTime end)
    {
        var token = await AcquireGraphTokenAsync(CalendarGraphScopes);
        const string timeZone = "W. Europe Standard Time";
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/events");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Content = JsonContent.Create(new
        {
            subject,
            body = new { contentType = "Text", content = description },
            start = new
            {
                dateTime = start.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                timeZone
            },
            end = new
            {
                dateTime = end.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                timeZone
            }
        });

        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Microsoft Graph meldet {(int)response.StatusCode}: {responseBody}");
        }
    }

    private async Task<AuthenticationResult> AcquireGraphTokenAsync(string[] scopes)
    {
        var clientId = _preferences.Authentication.ClientId;
        var tenantId = _preferences.Authentication.TenantId;

        if (!Guid.TryParse(clientId, out _) || !Guid.TryParse(tenantId, out _))
            throw new InvalidOperationException(
                "Bitte zuerst Client-ID und Tenant-ID in den Einstellungen unter Microsoft 365 eintragen.");

        _identity ??= PublicClientApplicationBuilder
            .Create(clientId!)
            .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
            .WithDefaultRedirectUri()
            .Build();

        var account = (await _identity.GetAccountsAsync()).FirstOrDefault();
        try
        {
            return await _identity.AcquireTokenSilent(scopes, account).ExecuteAsync();
        }
        catch (MsalUiRequiredException)
        {
            return await _identity.AcquireTokenInteractive(scopes)
                .WithParentActivityOrWindow(new WindowInteropHelper(this).Handle)
                .WithUseEmbeddedWebView(false)
                .ExecuteAsync();
        }
    }

    private void PostResult(int index, bool success, string message)
    {
        Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "draftResult",
            index,
            success,
            message
        }));
    }

    private void PostCalendarResult(int index, bool success, string message)
    {
        Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "calendarResult",
            index,
            success,
            message
        }));
    }

    private async Task CheckForUpdatesAsync(bool showStatus)
    {
        if (_updateCheckInProgress)
            return;

        _updateCheckInProgress = true;
        try
        {
            var update = await _updateManager.CheckForUpdatesAsync();
            if (update is null)
            {
                if (showStatus)
                    PostUpdateStatus("Du verwendest bereits die aktuelle Version.");
                return;
            }

            var choice = MessageBox.Show(
                this,
                "Eine neue Version des Tafel E-Mail-Assistenten ist verfügbar. Jetzt herunterladen und installieren?",
                "Update verfügbar",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (choice != MessageBoxResult.Yes)
            {
                if (showStatus)
                    PostUpdateStatus("Das Update ist verfügbar und kann später installiert werden.");
                return;
            }

            PostUpdateStatus("Update wird heruntergeladen …");
            await _updateManager.DownloadUpdatesAsync(update);
            PostUpdateStatus("Update geladen. Die App wird jetzt neu gestartet.");
            _updateManager.ApplyUpdatesAndRestart(update);
        }
        catch (Exception ex)
        {
            if (showStatus)
                PostUpdateStatus($"Updateprüfung nicht möglich: {ex.Message}");
        }
        finally
        {
            _updateCheckInProgress = false;
        }
    }

    private void PostUpdateStatus(string message)
    {
        if (Browser.CoreWebView2 is null)
            return;

        Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "updateStatus",
            message
        }));
    }

    private void PostSettingsStatus(bool success, string message)
    {
        Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "settingsStatus",
            success,
            message
        }));
    }

    private static void OpenExternalLink(string address, Action cancelNavigation)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || !(uri.Scheme is "http" or "https" or "mailto" or "tel"))
            return;

        cancelNavigation();
        try
        {
            Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Link konnte nicht geöffnet werden", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}