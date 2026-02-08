using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.WinForms;
using SecureChat.Core;

namespace SecureChat.Tabs;

[Tab("/main/index.html", typeof(Factory))]
internal class MainTab : AbstractTab
{
    public class Factory : ITabFactory
    {
        public AbstractTab Create(WebView2 webView, ServiceProvider serviceProvider)
        {
            return new MainTab(webView, serviceProvider.GetRequiredService<BackendAdapter>(), serviceProvider.GetRequiredService<CurrentSession>());
        }
    }

    private readonly WebView2 _webView;
    private readonly BackendAdapter _backendAdapter;
    private readonly CurrentSession _session;

    public MainTab(WebView2 webView, BackendAdapter backendAdapter, CurrentSession session)
    {
        _webView = webView;
        _backendAdapter = backendAdapter;
        _session = session;
    }

    public override void PageLoaded()
    {
        UpdateSettings(_backendAdapter.ServerUrl);
    }

    public void AuthSuccess()
    {
        _webView.Invoke(() =>
        {
            // Вызываем универсальный метод успеха
            _webView.CoreWebView2.ExecuteScriptAsync("window.api.processSuccess();");
        });
    }

    public void ShowError(string message)
    {
        _webView.Invoke(() =>
        {
            // Сериализация гарантирует корректную передачу строк с кавычками и спецсимволами
            _webView.CoreWebView2.ExecuteScriptAsync($"window.api.processError({JsonSerializer.Serialize(message)});");
        });
    }

    public void UpdateSettings(string serverUrl)
    {
        _webView.Invoke(() =>
        {
            _webView.CoreWebView2.ExecuteScriptAsync($"window.api.initSettings({JsonSerializer.Serialize(serverUrl)});");
        });
    }

    public override void ProcessPostMessage(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("action", out var actionProp) || actionProp.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var action = actionProp.GetString();
            switch (action)
            {
                default:
                    throw new Exception($"Unexpected action: {action}");

                case "SAVE_SETTINGS":
                    ProcessSaveSettings(doc.RootElement.Deserialize<SaveSettings>() ?? throw new Exception("Failed to deserialize 'SAVE_SETTINGS' message"));
                    break;

                case "CREATE_ROOM":
                    ProcessCreateRoom(doc.RootElement.Deserialize<CreateRoom>() ?? throw new Exception("Failed to deserialize 'CREATE_ROOM' message"));
                    break;

                case "JOIN_ROOM":
                    ProcessJoinRoom(doc.RootElement.Deserialize<JoinRoom>() ?? throw new Exception("Failed to deserialize 'JOIN_ROOM' message"));
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString());
        }
    }

    public class SaveSettings
    {
        [JsonPropertyName("serverUrl")]
        public string ServerUrl { get; set; } = string.Empty;
    }

    private void ProcessSaveSettings(SaveSettings request)
    {
        _backendAdapter.ServerUrl = request.ServerUrl;
    }

    public class CreateRoom
    {
        [JsonPropertyName("room")]
        public string RoomName { get; set; } = string.Empty;
        [JsonPropertyName("user")]
        public string Username { get; set; } = string.Empty;
        [JsonPropertyName("pass")]
        public string Password { get; set; } = string.Empty;
    }

    private void ProcessCreateRoom(CreateRoom request)
    {
        Task.Run(async () =>
        {
            try
            {
                await _backendAdapter.CreateChat(request.RoomName, request.Password);
                var session = await _backendAdapter.JoinChat(request.RoomName, request.Password);
                _session.Username = request.Username;
                _session.Session = session;
                AuthSuccess();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        });
    }

    public class JoinRoom
    {
        [JsonPropertyName("room")]
        public string RoomName { get; set; } = string.Empty;
        [JsonPropertyName("user")]
        public string Username { get; set; } = string.Empty;
        [JsonPropertyName("pass")]
        public string Password { get; set; } = string.Empty;
    }

    private void ProcessJoinRoom(JoinRoom request)
    {
        Task.Run(async () =>
        {
            try
            {
                var session = await _backendAdapter.JoinChat(request.RoomName, request.Password);
                _session.Username = request.Username;
                _session.Session = session;
                AuthSuccess();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        });
    }
}
