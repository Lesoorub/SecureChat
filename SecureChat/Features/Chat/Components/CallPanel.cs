using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SecureChat.Core;
using SecureChat.Core.Attributes;

namespace SecureChat.Features.Chat.Components;

internal class CallPanel : IDisposable
{
    private readonly ChatTab _tab;
    private readonly CurrentSession _currentSession;
    private readonly string _myUserId = Guid.NewGuid().ToString();

    private bool _micEnabled = false;

    private static readonly WaveFormat s_networkFormat = new WaveFormat(16000, 16, 1);
    private AudioInput _audioInput;
    private AudioOutput _audioOutput;

    private DateTime _lastAudioDataSend;

    private string? _inDeviceId;
    private string? _outDeviceId;

    class DisplayedUser
    {
        public DateTime LastPong;
        public DateTime LastSpeak;
        public string Username;

        public DisplayedUser(DateTime lastPong, string username)
        {
            LastPong = lastPong;
            Username = username;
        }
    }

    private readonly ConcurrentDictionary<string/*username*/, DisplayedUser> _users = new();
    private static readonly TimeSpan s_updateParticipantInterval = TimeSpan.FromSeconds(3);

    private readonly CancellationToken _cancellationToken;

    private CancellationTokenSource? _updateParticipantCts;

    public CallPanel(ChatTab tab, CurrentSession currentSession, CancellationToken cancellationToken)
    {
        _tab = tab;
        _currentSession = currentSession;
        _cancellationToken = cancellationToken;

        tab.RegisterNetCallback<AudioMessage>(AudioMessage.ACTION, Process);
        tab.RegisterNetCallback<JoinCall>(JoinCall.ACTION, Process);
        tab.RegisterNetCallback<WhoIsThere>(WhoIsThere.ACTION, Process);
        tab.RegisterNetCallback<IAmHere>(IAmHere.ACTION, Process);

        _audioInput = new AudioInput(_inDeviceId, s_networkFormat);
        _audioInput.AudioData += _audioInput_AudioData;
        _audioOutput = new AudioOutput(_outDeviceId, s_networkFormat);
        /*
function volumeChanged() {
    let newVolume = parseFloat(document.getElementById('volSlider').value);
    console.log(newVolume)
    // Обновляем текст на странице
    document.getElementById('val').innerText = newVolume;

    postToCSharp('volumeChanged', { value: newVolume / 100 });
}
            <div class="container">
                <label>Громкость микрофона: <span id="val">100</span>%</label>
                <!-- min 0 (0.0), max 200 (2.0), step 1 для плавности -->
                <input type="range" min="0" max="1000" value="100" id="volSlider" oninput="volumeChanged()">
            </div>

                case "volumeChanged":
                    ChangeVolume(doc.RootElement.GetProperty("value").GetSingle());
                    break;
         */
    }

    public void OnPageLoaded()
    {
        Task.Run(HeartbeatLoop);
    }

    private async Task HeartbeatLoop()
    {
        while (!_cancellationToken.IsCancellationRequested)
        {
            try
            {
                // 1. Запрашиваем актуальный список у сервера
                await _tab.Send(new WhoIsThere());

                var now = DateTime.UtcNow;
                var myUsername = string.IsNullOrWhiteSpace(_currentSession.Username) ? "Вы" : _currentSession.Username;

                // 2. Формируем список участников с их статусом речи
                var participants = new List<object>();

                // Добавляем себя
                participants.Add(new
                {
                    Id = _myUserId,
                    Name = myUsername,
                    IsSpeaking = (now - _lastAudioDataSend) < TimeSpan.FromMilliseconds(800)
                });

                // Добавляем остальных (только живых)
                foreach (var (id, user) in _users)
                {
                    if (now - user.LastPong < s_updateParticipantInterval * 2)
                    {
                        participants.Add(new
                        {
                            Id = id,
                            Name = user.Username,
                            IsSpeaking = (now - user.LastSpeak) < TimeSpan.FromMilliseconds(800)
                        });
                    }
                }

                // 3. Отправляем ОДИН пакет данных
                _tab.PostMessage(new { action = "sync_participants", participants = participants });
            }
            catch { /* ignore */ }

            await Task.Delay(500); // Оптимальный интервал для UI
        }
    }

    #region UI_ACTIONS

    private void EnableMic()
    {
        _audioInput.Enable();
    }

    private void _audioInput_AudioData(ArraySegment<byte> buffer)
    {
        _lastAudioDataSend = DateTime.UtcNow;
        _tab.Send(new AudioMessage()
        {
            UserId = _myUserId,
            Data = buffer.ToArray(), // Копируем на всякий
        });
        //Console.WriteLine($"Sended {buffer.Count} audio bytes");
    }

    private void TryDisableMic()
    {
        _audioInput.Disable();
    }

    private void EnableSpeaker()
    {
        _audioOutput.Enable();
    }

    private void TryDisableSpeaking()
    {
        _audioOutput.Disable();
    }

    private void SetSpeakerVolume(float volume)
    {
        _audioOutput.Volume = volume;
    }

    private void SetMicVolume(float volume)
    {
        _audioInput.Volume = volume;
    }

    #endregion

    #region NET

    /// <summary>
    /// Кто-то что-то сказал.
    /// </summary>
    /// <param name="request"></param>
    void Process(AudioMessage request)
    {
        //Console.WriteLine($"Received {request.Data.Count} audio bytes");
        if (request.UserId is not null && 
            _audioOutput is not null && 
            _audioOutput.AddAudioData(request.Data) && 
            _users.TryGetValue(request.UserId, out var user))
        {
            user.LastSpeak = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Срабатывает если кто-то подключился.
    /// </summary>
    void Process(JoinCall request)
    {
        if (request.Username is not null && request.UserId is not null)
        {
            var user = _users.GetOrAdd(request.UserId, x => new DisplayedUser(DateTime.UtcNow, request.Username));
            user.LastPong = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Кто-то спрашивает кто тут.
    /// </summary>
    void Process(WhoIsThere request)
    {
        if (_updateParticipantCts is null)
        {
            return;
        }

        _tab.Send(new IAmHere
        {
            UserId = _myUserId,
            Username = _currentSession.Username,
        });
    }

    /// <summary>
    /// Кто-то ответил что он тут есть.
    /// </summary>
    /// <param name="request"></param>
    void Process(IAmHere request)
    {
        if (request.Username is not null && request.UserId is not null)
        {
            var user = _users.GetOrAdd(request.UserId, x => new DisplayedUser(DateTime.UtcNow, request.Username));
            user.LastPong = DateTime.UtcNow;
        }
    }

    public class AudioMessage
    {
        public const string ACTION = "audio";

        [JsonPropertyName("action")]
        public string? Action { get; set; } = ACTION;
        [JsonPropertyName("userid")]
        public string? UserId { get; set; }
        [JsonPropertyName("data")]
        [JsonConverter(typeof(ArraySegmentByteConverter))]
        public ArraySegment<byte> Data { get; set; }
    }

    public class JoinCall
    {
        public const string ACTION = "join_call";

        [JsonPropertyName("action")]
        public string? Action { get; set; } = ACTION;

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("userid")]
        public string? UserId { get; set; }
    }

    public class WhoIsThere
    {
        public const string ACTION = "who_is_here";

        [JsonPropertyName("action")]
        public string? Action { get; set; } = ACTION;
    }

    public class IAmHere
    {
        public const string ACTION = "i_am_here";

        [JsonPropertyName("action")]
        public string? Action { get; set; } = ACTION;

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("userid")]
        public string? UserId { get; set; }
    }

    #endregion

    #region UI_EVENTS

    [JsAction("start_call")]
    internal void Process(StartCall _)
    {
        _tab.Send(new JoinCall()
        {
            UserId = _myUserId,
            Username = _currentSession.Username,
        });
        EnableSpeaker();
        EnableUpdateParticipant();
    }

    [JsAction("leave_call")]
    internal void Process(LeaveCall _)
    {
        DisableUpdateParticipant();
        TryDisableSpeaking();
    }

    [JsAction("toggle_mic")]
    internal void Process(ToggleMic _)
    {
        _micEnabled = !_micEnabled;
        SetMicState(_micEnabled);

        if (_micEnabled)
        {
            EnableMic();
        }
        else
        {
            TryDisableMic();
        }
    }

    [JsAction("open_settings")]
    internal void Process(OpenSettings _)
    {
        using var enumerator = new MMDeviceEnumerator();

        // Получаем устройства ввода (микрофоны)
        var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d => new AudioDevice { Id = d.ID, Name = d.FriendlyName })
            .ToList();

        // Получаем устройства вывода (динамики/наушники)
        var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d => new AudioDevice { Id = d.ID, Name = d.FriendlyName })
            .ToList();

        FillAudioDevices(captureDevices, renderDevices);
    }

    [JsAction("apply_settings")]
    internal void Process(ApplySettings request)
    {
        // Сохраняем ID устройств в конфиг или поля класса
        string selectedMicId = request.MicId;
        string selectedSpeakerId = request.SpeakerId;

        _inDeviceId = selectedMicId;
        _outDeviceId = selectedSpeakerId;

        SetSpeakerVolume(request.Volume);
        SetMicVolume(request.MicGain);
    }

    private void EnableUpdateParticipant()
    {
        DisableUpdateParticipant();
        var cts = _updateParticipantCts = new CancellationTokenSource();
        CancellationToken token = cts.Token;
        Task.Run(UpdateParticipant);

        async Task UpdateParticipant()
        {
            while (!_cancellationToken.IsCancellationRequested && !token.IsCancellationRequested)
            {
                try
                {
                    await _tab.Send(new WhoIsThere());
                    await Task.Delay(s_updateParticipantInterval);
                }
                catch (Exception ex)
                {
#if DEBUG
                    MessageBox.Show(ex.ToString());
#endif
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }
        }
    }

    private void DisableUpdateParticipant()
    {
        if (_updateParticipantCts is not null)
        {
            _updateParticipantCts.Cancel();
            _updateParticipantCts.Dispose();
            _updateParticipantCts = null;
        }
    }

    void SetMicState(bool state)
    {
        _tab.PostMessage(new { action = "set_mic_state", value = state });
    }

    void FillAudioDevices(List<AudioDevice> captureDevices, List<AudioDevice> renderDevices)
    {
        _tab.PostMessage(new { action = "fill_audio_devices", mics = captureDevices, speakers = renderDevices });
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

    public void Dispose()
    {
        _audioInput.Dispose();
        _audioOutput.Dispose();
    }

    public class Participant
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

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

    public class OpenSettings
    {

    }

    public class ApplySettings
    {
        [JsonPropertyName("micId")]
        public string? MicId { get; set; }

        [JsonPropertyName("speakerId")]
        public string? SpeakerId { get; set; }

        [JsonPropertyName("volume")]
        public float Volume { get; set; } // 0.0-1.0

        [JsonPropertyName("micGain")]
        public float MicGain { get; set; } // 0.0-1.0

        [JsonPropertyName("activation")]
        public string? Activation { get; set; } // "voice", "push-to-talk", "always"

        [JsonPropertyName("threshold")]
        public int Threshold { get; set; } // 0-100

        [JsonPropertyName("pttKey")]
        public string? PttKey { get; set; } // Например, "Space" или "KeyV"
    }

    #endregion
}
public class ArraySegmentByteConverter : JsonConverter<ArraySegment<byte>>
{
    public override ArraySegment<byte> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // System.Text.Json serializes byte[] as Base64 strings by default.
        // We read the Base64 string and convert it back to a byte array.
        if (reader.TokenType == JsonTokenType.String)
        {
            byte[] byteArray = reader.GetBytesFromBase64();
            return new ArraySegment<byte>(byteArray);
        }

        // Handle other cases (e.g., if it was manually serialized as a JSON array of numbers)
        // by throwing an exception or implementing custom logic.
        throw new JsonException("Expected Base64 string for ArraySegment<byte>.");
    }

    public override void Write(Utf8JsonWriter writer, ArraySegment<byte> value, JsonSerializerOptions options)
    {
        // Write the ArraySegment<byte> value to the writer as a Base64 string.
        // This is the standard way System.Text.Json handles binary data.
        writer.WriteBase64StringValue(value.AsSpan());
    }
}
