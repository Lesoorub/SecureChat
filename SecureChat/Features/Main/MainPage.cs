using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SecureChat.Core;
using SecureChat.Core.Attributes;
using SecureChat.Core.Interfaces;
using SecureChat.UI.Base;
using static SecureChat.Core.BackendAdapter;

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
        UpdateVersionFromServer();
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

    private void UpdateVersionFromServer()
    {
        Task.Run(async () =>
        {
            var version = await _backendAdapter.GetVersion();
            await _webView.ExecuteScriptAsync($"window.api.setVersion({JsonSerializer.Serialize(version)});");
        });
    }

    [JsAction("DOWNLOAD_UPDATE")]
    internal void StartUpdateDownload()
    {
        Task.Run(async () =>
        {
            // 1. Формируем путь к папке updates рядом с .exe
            string updateDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "updates");
            string filePath = Path.Combine(updateDir, "latest.7z");

            try
            {
                // Создаем директорию, если её нет
                if (!Directory.Exists(updateDir))
                    Directory.CreateDirectory(updateDir);

                // Если старый файл остался — удаляем перед началом
                if (File.Exists(filePath)) File.Delete(filePath);

                // 2. Скачивание
                await _backendAdapter.GetLatest(filePath, (in DownloadProgress p) =>
                {
                    _webView.ExecuteScriptAsync($"document.getElementById('btn-download').textContent = 'Загрузка {p.Percent:N1}%';");
                });

                await _webView.ExecuteScriptAsync("document.getElementById('btn-download').textContent = 'Успешно. Нажмите чтобы повторить загрузку.';");

                // 3. Открываем папку в проводнике и выделяем файл
                // Аргумент /select позволяет не просто открыть папку, а подсветить файл
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                await _webView.ExecuteScriptAsync($"window.api.processError('Ошибка: {ex.Message}');");
            }
        });
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
        UpdateVersionFromServer();
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
