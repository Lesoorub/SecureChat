const settingsModal = document.getElementById('settings-modal');
const micSelect = document.getElementById('mic-select');
const speakerSelect = document.getElementById('speaker-select');
const volRange = document.getElementById('volume-range');
const gainRange = document.getElementById('mic-gain-range');
const activationType = document.getElementById('activation-type');
const thresholdRange = document.getElementById('threshold-range');
const pttInput = document.getElementById('ptt-key-input');
const thresholdCont = document.getElementById('voice-threshold-container');
const pttCont = document.getElementById('ptt-key-container');

function postToCSharp(action, data = {}) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage({ action, ...data });
    }
}

// Слушаем ответ от C#
window.chrome.webview.addEventListener('message', event => {
    const data = event.data;

    switch (data.action) {
        case "fill_audio_devices":
            fillAudioDevices(data.mics, data.speakers);
            break;
    }
});

document.getElementById('close-settings').onclick = applySettingsAndClose;
document.getElementById('save-settings').onclick = applySettingsAndClose;

function applySettingsAndClose() {
    postToCSharp('apply_settings', {
        micId: micSelect.value,
        speakerId: speakerSelect.value,
        volume: parseFloat(volRange.value),
        micGain: parseFloat(gainRange.value),
        activation: activationType.value,
        threshold: parseInt(thresholdRange.value),
        pttKey: pttInput.value
    });

    // Получаем экземпляр модалки Bootstrap и закрываем её
    const modalElement = document.getElementById('settingsModal');
    const modalInstance = bootstrap.Modal.getOrCreateInstance(modalElement);
    modalInstance.hide();
}

// Функция для C#, чтобы заполнить списки устройств
function fillAudioDevices(mics, speakers) {
    const fill = (select, items) => {
        select.innerHTML = items.map(i => `<option value="${i.id}">${i.name}</option>`).join('');
    };
    fill(micSelect, mics);
    fill(speakerSelect, speakers);
}

// Логика переключения типов активации
activationType.onchange = () => {
    thresholdCont.classList.toggle('d-none', activationType.value !== 'voice');
    pttCont.classList.toggle('d-none', activationType.value !== 'push-to-talk');
};

// Захват клавиши для PTT
pttInput.onkeydown = (e) => {
    e.preventDefault();
    pttInput.value = e.code; // Сохраняем код клавиши (напр. Space, KeyV)
};

window.changeMax = (id, delta) => {
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
};

// Отображение значений ползунков
const updateLabel = (input, labelId, suffix = '') => {
    input.oninput = () => document.getElementById(labelId).textContent = input.value + suffix;
};
updateLabel(volRange, 'vol-val');
updateLabel(gainRange, 'gain-val');
updateLabel(thresholdRange, 'threshold-val');

const setupSlider = (inputId, labelId) => {
    const input = document.getElementById(inputId);
    const label = document.getElementById(labelId);
    input.addEventListener('input', () => {
        label.textContent = input.value;
    });
};

setupSlider('volume-range', 'vol-val');
setupSlider('mic-gain-range', 'gain-val');
setupSlider('threshold-range', 'threshold-val');