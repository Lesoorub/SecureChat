using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SecureChat.Core;
using SecureChat.Core.Attributes;
using SecureChat.Core.Interfaces;
using SecureChat.UI.Base;

namespace SecureChat.Features.Main;

[Page("/pages/main/index.html", initPage: true)]
internal class MainPage : AbstractPage
{
    private readonly IWebView _webView;
    private readonly BackendAdapter _backendAdapter;
    private readonly CurrentSession _session;

    public MainPage(ILogger<MainPage> logger, IWebView webView, BackendAdapter backendAdapter, CurrentSession session)
        : base(logger)
    {
        _webView = webView;
        _backendAdapter = backendAdapter;
        _session = session;
        session.Reset();

        InitializeActions();
    }

    public override void PageLoaded()
    {
        UpdateSettings(_backendAdapter.ServerUrl);
    }

    public void AuthSuccess()
    {
        // Вызываем универсальный метод успеха
        _webView.ExecuteScriptAsync("window.api.processSuccess();");
    }

    public void ShowError(string message)
    {
        // Сериализация гарантирует корректную передачу строк с кавычками и спецсимволами
        _webView.ExecuteScriptAsync($"window.api.processError({JsonSerializer.Serialize(message)});");
    }

    public void UpdateSettings(string serverUrl)
    {
        _webView.ExecuteScriptAsync($"window.api.initSettings({JsonSerializer.Serialize(serverUrl)});");
    }

    public class SaveSettings
    {
        [JsonPropertyName("serverUrl")]
        public string ServerUrl { get; set; } = string.Empty;
    }

    [JsAction("SAVE_SETTINGS")]
    internal void ProcessSaveSettings(SaveSettings request)
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

    [JsAction("CREATE_ROOM")]
    internal void ProcessCreateRoom(CreateRoom request)
    {
        Task.Run(async () =>
        {
            try
            {
                await _backendAdapter.CreateChat(request.RoomName, request.Password);
                var session = await _backendAdapter.JoinChat(request.RoomName, request.Password);
                _session.Set(request.Username, session);
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

    [JsAction("JOIN_ROOM")]
    internal void ProcessJoinRoom(JoinRoom request)
    {
        Task.Run(async () =>
        {
            try
            {
                var session = await _backendAdapter.JoinChat(request.RoomName, request.Password);
                _session.Set(request.Username, session);
                AuthSuccess();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        });
    }
}
