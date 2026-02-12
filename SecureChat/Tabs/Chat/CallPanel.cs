using System.Text.Json;
using System.Text.Json.Serialization;
using NAudio.CoreAudioApi;
using SecureChat.Core;

namespace SecureChat.Tabs.Chat;

internal class CallPanel
{
    private readonly ChatTab _tab;
    private readonly CurrentSession _currentSession;

    public CallPanel(ChatTab tab, CurrentSession currentSession)
    {
        _tab = tab;
        _currentSession = currentSession;

        tab.RegisterPostMsgCallback("start_call", x => Process(x.Deserialize<StartCall>() ?? throw new Exception("Cannot deserialize ui message")));
        tab.RegisterPostMsgCallback("toggle_mic", x => Process(x.Deserialize<ToggleMic>() ?? throw new Exception("Cannot deserialize ui message")));
        tab.RegisterPostMsgCallback("get_audio_devices", x => Process(x.Deserialize<GetAudioDevices>() ?? throw new Exception("Cannot deserialize ui message")));
        tab.RegisterPostMsgCallback("set_audio_devices", x => Process(x.Deserialize<SetAudioDevices>() ?? throw new Exception("Cannot deserialize ui message")));
        tab.RegisterPostMsgCallback("leave_call", x => Process(x.Deserialize<LeaveCall>() ?? throw new Exception("Cannot deserialize ui message")));
    }

    public void OnPageLoaded()
    {
    }

    void Process(StartCall request)
    {
        var list = new List<Participant> {
            new Participant { Id = "1", Name = string.IsNullOrWhiteSpace(_currentSession.Username) ? "Вы" : _currentSession.Username },
            new Participant { Id = "2", Name = "Alex Smith" }
        };
        UpdateParticipants(new ParticipantsUpdate { Participants = list });
    }

    void Process(ToggleMic request)
    {

    }

    void Process(GetAudioDevices request)
    {
        var enumerator = new MMDeviceEnumerator();

        // Получаем устройства ввода (микрофоны)
        var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d => new AudioDevice { Id = d.ID, Name = d.FriendlyName })
            .ToList();

        // Получаем устройства вывода (динамики/наушники)
        var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d => new AudioDevice { Id = d.ID, Name = d.FriendlyName })
            .ToList();

        // Сериализуем и отправляем в JS
        string micsJson = JsonSerializer.Serialize(captureDevices);
        string speakersJson = JsonSerializer.Serialize(renderDevices);

        _tab.ExecuteScript($"window.fillAudioDevices({micsJson}, {speakersJson})");
    }

    void Process(SetAudioDevices request)
    {
        // Сохраняем ID устройств в конфиг или поля класса
        string selectedMicId = request.MicId;
        string selectedSpeakerId = request.SpeakerId;

        // Пример инициализации вывода по ID (CoreAudio):
        // var enumerator = new MMDeviceEnumerator();
        // var device = enumerator.GetDevice(selectedSpeakerId);
        // var output = new WasapiOut(device, AudioClientShareMode.Shared, false, 100);
    }

    void Process(LeaveCall request)
    {

    }

    void UpdateParticipants(ParticipantsUpdate participantsData)
    {
        // Передаем список участников в JS
        string jsonParticipants = JsonSerializer.Serialize(participantsData.Participants);
        _tab.ExecuteScript($"window.updateParticipants({jsonParticipants})");
    }

    void SetUserSpeaking(UserSpeaking speakingData)
    {
        // Подсвечиваем говорящего в интерфейсе
        _tab.ExecuteScript($"window.setSpeaking('{speakingData.UserId}', {speakingData.IsSpeaking.ToString().ToLower()})");
    }

    public void ShowCallPanel()
    {
        // Показать панель звонка в JS
        _tab.ExecuteScript("document.getElementById('call-panel').classList.remove('hidden')");
    }

    public void HideCallPanel()
    {
        // Скрыть панель звонка в JS
        _tab.ExecuteScript("document.getElementById('call-panel').classList.add('hidden')");
    }

    public class Participant
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("isSpeaking")]
        public bool IsSpeaking { get; set; } = false;
    }

    public class ParticipantsUpdate
    {
        [JsonPropertyName("action")]
        public string Action { get; set; } = "update_participants";

        [JsonPropertyName("participants")]
        public List<Participant> Participants { get; set; } = new();
    }

    public class UserSpeaking
    {
        [JsonPropertyName("action")]
        public string Action { get; set; } = "user_speaking";

        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = string.Empty;

        [JsonPropertyName("is_speaking")]
        public bool IsSpeaking { get; set; }
    }

    // Для передачи списка устройств в JS модалку
    public class AudioDevice
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class StartCall { }
    public class LeaveCall { }

    public class ToggleMic
    {
        [JsonPropertyName("active")] public bool Active { get; set; }
    }

    public class GetAudioDevices { }

    public class SetAudioDevices
    {
        [JsonPropertyName("micId")] public string MicId { get; set; }
        [JsonPropertyName("speakerId")] public string SpeakerId { get; set; }
    }
}
