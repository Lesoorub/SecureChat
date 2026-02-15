using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IO;
using Microsoft.VisualBasic.ApplicationServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SecureChat.Core;

namespace SecureChat.Tabs.Chat;

internal class CallPanel
{
    private readonly ChatTab _tab;
    private readonly CurrentSession _currentSession;
    private readonly string _myUserId = Guid.NewGuid().ToString();

    private bool _micEnabled = false;

    private WasapiCapture? _waveIn;
    private WasapiOut? _waveOut;
    private BufferedWaveProvider? _waveProvider;
    private VolumeSampleProvider? _volumeControl;

    private static readonly WaveFormat s_networkFormat = WaveFormat.CreateIeeeFloatWaveFormat(16000, 1);
    private BufferedWaveProvider? _captureBuffer;
    private MediaFoundationResampler? _captureResampler;
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
    private static readonly TimeSpan s_updateParticipantInterval = TimeSpan.FromSeconds(5);

    private readonly CancellationToken _cancellationToken;

    private CancellationTokenSource? _updateParticipantCts;

    public CallPanel(ChatTab tab, CurrentSession currentSession, CancellationToken cancellationToken)
    {
        _tab = tab;
        _currentSession = currentSession;
        _cancellationToken = cancellationToken;

        tab.RegisterUiCallback<StartCall>("start_call", Process);
        tab.RegisterUiCallback<ToggleMic>("toggle_mic", Process);
        tab.RegisterUiCallback<OpenSettings>("open_settings", Process);
        tab.RegisterUiCallback<ApplySettings>("apply_settings", Process);
        tab.RegisterUiCallback<LeaveCall>("leave_call", Process);

        tab.RegisterNetCallback<AudioMessage>(AudioMessage.ACTION, Process);
        tab.RegisterNetCallback<JoinCall>(JoinCall.ACTION, Process);
        tab.RegisterNetCallback<WhoIsThere>(WhoIsThere.ACTION, Process);
        tab.RegisterNetCallback<IAmHere>(IAmHere.ACTION, Process);

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
        // Поток отображения стипикеров.
        Task.Run(async () =>
        {
            var speakUpdateInterval = TimeSpan.FromMilliseconds(500);
            while (!_cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    SetUsersSpeaking(_users.Select(user =>
                    {
                        return new UserSpeaking
                        {
                            UserId = user.Key,
                            IsSpeaking = (now - user.Value.LastSpeak) < speakUpdateInterval * 2,
                        };
                    }));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }
                finally
                {
                    await Task.Delay(speakUpdateInterval);
                }
            }
        });
        Task.Run(async () =>
        {
            var speakUpdateInterval = TimeSpan.FromMilliseconds(100);
            bool lastState = false;
            while (!_cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var isSpeaking = (DateTime.UtcNow - _lastAudioDataSend) < speakUpdateInterval * 2;
                    if (isSpeaking != lastState)
                    {
                        lastState = isSpeaking;
                        SetSpeaking(userId: _myUserId, isSpeaking: lastState);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }
                finally
                {
                    await Task.Delay(speakUpdateInterval);
                }
            }
        });
    }

    #region UI_ACTIONS

    private void EnableMic()
    {
        TryDisableMic();

        if (string.IsNullOrEmpty(_inDeviceId))
        {
            _waveIn = new WasapiCapture();
        }
        else
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(_inDeviceId);
            _waveIn = new WasapiCapture(device);
        }

        // 1. Буфер для сырых данных с микрофона (в его родном формате)
        _captureBuffer = new BufferedWaveProvider(_waveIn.WaveFormat) { ReadFully = false };

        // 2. Ресемплер: из формата микрофона -> в ваш сетевой 48кГц/1канал
        _captureResampler = new MediaFoundationResampler(_captureBuffer, s_networkFormat);
        _captureResampler.ResamplerQuality = 60; // Хорошее качество

        _waveIn.DataAvailable += HandleAudioData;
        _waveIn.StartRecording();
    }

    private void TryDisableMic()
    {
        if (_waveIn is not null)
        {
            _waveIn.StopRecording();
            _waveIn.DataAvailable -= HandleAudioData;
            _waveIn.Dispose();
            _waveIn = null;
        }
    }

    private void HandleAudioData(object? obj, WaveInEventArgs args)
    {
        if (_captureBuffer is null ||  _captureResampler is null)
        {
            return;
        }

        // Сначала кладем данные в промежуточный буфер
        _captureBuffer.AddSamples(args.Buffer, 0, args.BytesRecorded);

        // Читаем из ресемплера уже преобразованные данные
        byte[] outBuffer = new byte[args.BytesRecorded]; // Размер с запасом
        int read = _captureResampler.Read(outBuffer, 0, outBuffer.Length);
        if (read > 0)
        {
            _lastAudioDataSend = DateTime.UtcNow;
            _tab.Send(new AudioMessage()
            {
                UserId = _myUserId,
                Data = new ArraySegment<byte>(outBuffer, 0, read),
            });
        }
    }

    private void EnableSpeaker()
    {
        if (string.IsNullOrEmpty(_outDeviceId))
        {
            _waveOut = new WasapiOut();
        }
        else
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(_outDeviceId);
            _waveOut = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
        }

        _waveProvider = new BufferedWaveProvider(s_networkFormat)
        {
            DiscardOnBufferOverflow = true,
            ReadFully = true,
        };

        // 1. Превращаем байты в Float Sample Provider
        var sampleProvider = _waveProvider.ToSampleProvider();

        // 2. Ресемплируем и меняем количество каналов под целевое устройство (OutputWaveFormat)
        // Это магия, которая адаптирует 48000/1 в то, что хочет Windows (например, 44100/2)
        var resampler = new WdlResamplingSampleProvider(sampleProvider, _waveOut.OutputWaveFormat.SampleRate);

        // 3. Если устройство стерео, а входящий звук моно — приводим к стерео
        ISampleProvider finalProvider = resampler;
        if (_waveOut.OutputWaveFormat.Channels > 1 && s_networkFormat.Channels == 1)
        {
            finalProvider = new MonoToStereoSampleProvider(resampler);
        }

        _volumeControl = new VolumeSampleProvider(finalProvider) { Volume = 1.0f };
        _waveOut.PlaybackStopped += (s, e) =>
        {
            Console.WriteLine($"Playback Stopped! Reason: {e.Exception?.Message}");
        };
        _waveOut.Init(_volumeControl.ToWaveProvider()); // Инициализируем уже адаптированным провайдером
        _waveOut.Play();
    }

    private void TryDisableSpeaking()
    {
        if (_volumeControl is not null)
        {
            _volumeControl = null;
        }

        if (_waveOut is not null)
        {
            _waveOut.Stop();
            _waveOut.Dispose();
            _waveOut = null;
        }
    }

    // Вызывай этот метод из TrackBar.Scroll
    private void ChangeVolume(float volume)
    {
        if (_volumeControl != null)
            _volumeControl.Volume = volume; // Например, от 0.0f до 2.0f
    }

    #endregion

    #region NET

    /// <summary>
    /// Кто-то что-то сказал.
    /// </summary>
    /// <param name="request"></param>
    void Process(AudioMessage request)
    {
        if (_waveProvider is not null && request.UserId is not null)
        {
            _waveProvider.AddSamples(request.Data.Array, request.Data.Offset, request.Data.Count);
            Console.WriteLine($"Buffered bytes: {_waveProvider.BufferedBytes}");
            if (_users.TryGetValue(request.UserId, out var user))
            {
                user.LastSpeak = DateTime.UtcNow;
            }
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
            UpdateParticipantsFromUsers();
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
            UpdateParticipantsFromUsers();
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

    void Process(StartCall _)
    {
        _tab.Send(new JoinCall()
        {
            UserId = _myUserId,
            Username = _currentSession.Username,
        });
        EnableSpeaker();
        UpdateParticipantsFromUsers();
        EnableUpdateParticipant();
    }

    void Process(LeaveCall _)
    {
        DisableUpdateParticipant();
        TryDisableSpeaking();
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
                    _tab.Send(new WhoIsThere());
                    await Task.Delay(s_updateParticipantInterval);
                    UpdateParticipantsFromUsers();
                }
                catch (Exception ex)
                {
#if DEBUG
                    MessageBox.Show(ex.ToString());
#endif
                }
                finally
                {
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

    void Process(ToggleMic _)
    {
        _micEnabled = !_micEnabled;
        _tab.ExecuteScript($"window.setMicState({_micEnabled.ToString().ToLower()})");

        if (_micEnabled)
        {
            EnableMic();
        }
        else
        {
            TryDisableMic();
        }
    }

    void Process(OpenSettings _)
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

        // Сериализуем и отправляем в JS
        string micsJson = JsonSerializer.Serialize(captureDevices);
        string speakersJson = JsonSerializer.Serialize(renderDevices);

        _tab.ExecuteScript($"window.fillAudioDevices({micsJson}, {speakersJson})");
    }

    void Process(ApplySettings request)
    {
        // Сохраняем ID устройств в конфиг или поля класса
        string selectedMicId = request.MicId;
        string selectedSpeakerId = request.SpeakerId;

        _inDeviceId = selectedMicId;
        _outDeviceId = selectedSpeakerId;
    }

    void UpdateParticipantsFromUsers()
    {
        var myusername = string.IsNullOrWhiteSpace(_currentSession.Username) ? "Вы" : _currentSession.Username;
        var list = new List<Participant> {
            new() { Id = _myUserId, Name = myusername },
        };
        var now = DateTime.UtcNow;
        foreach (var (userId, user) in _users.OrderBy(x => x.Key))
        {
            if ((now - user.LastPong) < s_updateParticipantInterval * 2) // Если ответ был меньше чем два интервала опроса.
            {
                list.Add(new() { Id = userId, Name = user.Username });
            }
        }
        UpdateParticipants(new ParticipantsUpdate { Participants = list });
    }

    void UpdateParticipants(ParticipantsUpdate participantsData)
    {
        // Передаем список участников в JS
        string jsonParticipants = JsonSerializer.Serialize(participantsData.Participants);
        _tab.ExecuteScript($"window.updateParticipants({jsonParticipants})");
    }

    void SetSpeaking(string userId, bool isSpeaking)
    {
        _tab.ExecuteScript($"window.setSpeaking({JsonSerializer.Serialize(userId)}, {JsonSerializer.Serialize(isSpeaking)})");
    }

    void SetUsersSpeaking(IEnumerable<UserSpeaking> speakingList)
    {
        // Превращаем список в словарь { "userId": isSpeaking }
        var states = speakingList.ToDictionary(x => x.UserId, x => x.IsSpeaking);

        // Сериализуем в JSON
        string json = JsonSerializer.Serialize(states);

        // Вызываем JS один раз
        _tab.ExecuteScript($"window.setUsersSpeaking({json})");
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
        public int Volume { get; set; } // 0-100

        [JsonPropertyName("micGain")]
        public float MicGain { get; set; } // 0.0 - 5.0

        [JsonPropertyName("activation")]
        public string? Activation { get; set; } // "voice", "push-to-talk", "always"

        [JsonPropertyName("threshold")]
        public int Threshold { get; set; } // -100 to 0 (dB)

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