const settingsWindow = new class SettingsWindow {

    settingsModal = document.getElementById('settings-modal');
    micSelect = document.getElementById('mic-select');
    speakerSelect = document.getElementById('speaker-select');
    volRange = document.getElementById('volume-range');
    gainRange = document.getElementById('mic-gain-range');
    activationType = document.getElementById('activation-type');
    thresholdRange = document.getElementById('threshold-range');
    pttInput = document.getElementById('ptt-key-input');
    thresholdCont = document.getElementById('voice-threshold-container');
    pttCont = document.getElementById('ptt-key-container');
    closeSettings = document.getElementById('close-settings');
    saveSettings = document.getElementById('save-settings');

    actions = {
        fill_audio_devices: d => this.fillAudioDevices(d.mics, d.speakers),
        set_mic_volume: d => this.updateThresholdCont(d.value),
    };

    constructor() {
        // Слушаем ответ от C#
        window.chrome.webview.addEventListener('message', event => this.actions[event.data.action]?.(event.data));
        this.closeSettings.onclick = this.applySettingsAndClose;
        this.saveSettings.onclick = this.applySettingsAndClose;

        // Логика переключения типов активации
        this.activationType.onchange = () => {
            this.thresholdCont.classList.toggle('d-none', this.activationType.value !== 'voice');
            this.pttCont.classList.toggle('d-none', this.activationType.value !== 'push-to-talk');
        };

        // Захват клавиши для PTT
        this.pttInput.onkeydown = (e) => {
            e.preventDefault();
            this.pttInput.value = e.code; // Сохраняем код клавиши (напр. Space, KeyV)
        };

        this.updateLabel(this.volRange, 'vol-val');
        this.updateLabel(this.gainRange, 'gain-val');
        this.updateLabel(this.thresholdRange, 'threshold-val');

        this.setupSlider('volume-range', 'vol-val');
        this.setupSlider('mic-gain-range', 'gain-val');
        this.setupSlider('threshold-range', 'threshold-val');
    }

    postToCSharp = (action, data = {}) => {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage({ action, ...data });
        }
    }

    updateThresholdCont = (volume) => {
        const thresholdValue = parseFloat(this.thresholdRange.value); // 0 - 100
        const currentVolumePct = volume * 100; // 0 - 100

        // Формируем "прогресс-бар" громкости на фоне слайдера
        // Зеленый цвет показывает текущий gain, серый — остаток
        this.thresholdRange.style.background = `linear-gradient(to right, 
        #28a745 ${currentVolumePct}%, 
        #dee2e6 ${currentVolumePct}%)`;

        // Опционально: меняем цвет ползунка или фона, если громкость выше порога
        if (currentVolumePct > thresholdValue) {
            this.thresholdRange.style.boxShadow = '0 0 5px #dc3545'; // Подсветка при активации
        } else {
            this.thresholdRange.style.boxShadow = 'none';
        }
    }

    applySettingsAndClose = () => {
        this.postToCSharp('apply_settings', {
            micId: this.micSelect.value,
            speakerId: this.speakerSelect.value,
            volume: parseFloat(this.volRange.value),
            micGain: parseFloat(this.gainRange.value),
            activation: this.activationType.value,
            threshold: parseInt(this.thresholdRange.value),
            pttKey: this.pttInput.value
        });

        // Получаем экземпляр модалки Bootstrap и закрываем её
        const modalInstance = bootstrap.Modal.getOrCreateInstance(settingsModal);
        modalInstance.hide();
    }

    // Функция для C#, чтобы заполнить списки устройств
    fillAudioDevices = (mics, speakers) => {
        const fill = (select, items) => {
            select.innerHTML = items.map(i => `<option value="${i.id}">${i.name}</option>`).join('');
        };
        fill(this.micSelect, mics);
        fill(this.speakerSelect, speakers);
    }

    changeMax = (id, delta) => {
        const input = document.getElementById(id);
        const currentMax = parseFloat(input.max);
        // Ограничиваем, чтобы максимум не стал меньше или равен минимуму
        const newMax = Math.max(parseFloat(input.min) + 0.1, currentMax + delta);

        input.max = newMax;

        // Если текущее значение вылезло за новый максимум — подтягиваем его
        if (parseFloat(input.value) > newMax) {
            input.value = newMax;
        }

        // ВАЖНО: Принудительно вызываем событие 'input', чтобы обновились текстовые метки (span)
        input.dispatchEvent(new Event('input'));
    }

    // Отображение значений ползунков
    updateLabel = (input, labelId, suffix = '') => {
        input.oninput = () => document.getElementById(labelId).textContent = input.value + suffix;
    }

    setupSlider = (inputId, labelId) => {
        const input = document.getElementById(inputId);
        const label = document.getElementById(labelId);
        input.addEventListener('input', () => {
            label.textContent = input.value;
        });
    }
}()