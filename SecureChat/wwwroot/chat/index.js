const messagesContainer = document.getElementById('messages-container');
const messageInput = document.getElementById('message-input');
const sendBtn = document.getElementById('send-btn');
const callPanel = document.getElementById('call-panel');
const callStartBtn = document.getElementById('call-start-btn');
const settingsModal = document.getElementById('settings-modal');
const micSelect = document.getElementById('mic-select');
const speakerSelect = document.getElementById('speaker-select');
function postToCSharp(action, data = {}) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage({ action, ...data });
    }
}

window.appendMessage = (role, text, id = null, status = 'sent', senderName = 'Бот') => {
    const msgId = id || 'msg_' + Date.now();

    // Создаем обертку
    const wrapper = document.createElement('div');
    wrapper.className = `message-wrapper ${role}`;

    // Добавляем имя, если это чужое сообщение
    if (role === 'bot') {
        const nameDiv = document.createElement('div');
        nameDiv.className = 'sender-name';
        nameDiv.textContent = senderName;
        wrapper.appendChild(nameDiv);
    }

    // Создаем само сообщение
    const msgDiv = document.createElement('div');
    msgDiv.className = 'message';
    msgDiv.id = msgId;

    const textNode = document.createTextNode(text);
    msgDiv.appendChild(textNode);

    // Добавляем статус только пользователю
    if (role === 'user') {
        const statusSpan = document.createElement('span');
        statusSpan.className = `status ${status}`;
        msgDiv.appendChild(statusSpan);
    }

    wrapper.appendChild(msgDiv);
    messagesContainer.appendChild(wrapper);
    messagesContainer.scrollTo({ top: messagesContainer.scrollHeight, behavior: 'smooth' });
};

// Вызов из C#: updateMessageStatus('msg_123', 'sent') или 'error'
window.updateMessageStatus = (id, status) => {
    const msg = document.getElementById(id);
    if (msg) {
        const statusSpan = msg.querySelector('.status');
        if (statusSpan) {
            statusSpan.className = `status ${status}`;
        }
    }
};


function handleSend() {
    const text = messageInput.value.trim();
    if (text) {
        const tempId = 'msg_' + Date.now();
        // Добавляем сообщение со статусом 'pending'
        appendMessage('user', text, tempId, 'pending');

        postToCSharp('send_message', {
            text: text,
            id: tempId
        });

        messageInput.value = '';
    }
}

// Начать звонок (показать панель)
callStartBtn.onclick = () => {
    toggleCallUI(true);
    postToCSharp('start_call');
};

// Генерация стабильного цвета на основе строки (имени)
function stringToColor(str) {
    let hash = 0;
    for (let i = 0; i < str.length; i++) {
        hash = str.charCodeAt(i) + ((hash << 5) - hash);
    }
    const c = (hash & 0x00FFFFFF).toString(16).toUpperCase();
    return "#" + "00000".substring(0, 6 - c.length) + c;
}

// Получение инициалов (первые две буквы)
function getInitials(name) {
    const parts = name.trim().split(/\s+/); // Разбиваем по пробелам, игнорируя лишние

    if (parts.length === 1) {
        // Если одно слово — берем первые две буквы
        return parts[0].substring(0, 2).toUpperCase();
    }

    // Если слов несколько — берем первую букву каждого и склеиваем (до 2-х штук)
    return parts
        .map(n => n[0])
        .join('')
        .toUpperCase()
        .substring(0, 2);
}

window.updateParticipants = (participants) => {
    const list = document.getElementById('participants-list');
    const template = document.getElementById('participant-template');

    list.innerHTML = '';

    participants.forEach(p => {
        // Клонируем содержимое шаблона
        const fragment = template.content.cloneNode(true);
        const container = fragment.querySelector('.participant');
        const avatar = fragment.querySelector('.avatar');
        const nameLabel = fragment.querySelector('.participant-name');

        // Настройка контейнера и ID
        container.id = `user-${p.id}`;
        if (p.isSpeaking) container.classList.add('speaking');

        // Генерация визуалов
        const bgColor = stringToColor(p.name);
        const initials = getInitials(p.name);

        // Заполнение данными
        avatar.style.backgroundColor = bgColor;
        avatar.textContent = initials;
        nameLabel.textContent = p.name;

        list.appendChild(fragment);
    });
};

// Вызов из C# для индикации речи: setSpeaking('user_1', true)
window.setSpeaking = (userId, isSpeaking) => {
    const el = document.getElementById(`user-${userId}`);
    if (el) {
        isSpeaking ? el.classList.add('speaking') : el.classList.remove('speaking');
    }
};

// Обработка кнопок управления
document.getElementById('mic-btn').onclick = function () {
    this.classList.toggle('active');
    postToCSharp('toggle_mic', { active: !this.classList.contains('active') });
};

document.getElementById('hangup-btn').onclick = () => {
    toggleCallUI(false);
    postToCSharp('leave_call');
};

// Открытие модалки
document.getElementById('settings-btn').onclick = () => {
    postToCSharp('get_audio_devices'); // Запрашиваем список устройств у C#
    settingsModal.showModal();
};

// Закрытие
document.getElementById('close-settings').onclick = () => settingsModal.close();

// Применение настроек
document.getElementById('save-settings').onclick = () => {
    postToCSharp('set_audio_devices', {
        micId: micSelect.value,
        speakerId: speakerSelect.value
    });
    settingsModal.close();
};

// Функция для C#, чтобы заполнить списки устройств
window.fillAudioDevices = (mics, speakers) => {
    const fill = (select, items) => {
        select.innerHTML = items.map(i => `<option value="${i.id}">${i.name}</option>`).join('');
    };
    fill(micSelect, mics);
    fill(speakerSelect, speakers);
};

const toggleCallUI = (show) => {
    if (show) {
        callPanel.classList.remove('hidden');
    } else {
        callPanel.classList.add('hidden');
    }
    // Даем браузеру время пересчитать высоты, затем скроллим чат
    setTimeout(() => {
        messagesContainer.scrollTo({
            top: messagesContainer.scrollHeight,
            behavior: 'smooth'
        });
    }, 100);
};

sendBtn.onclick = handleSend;
messageInput.onkeypress = (e) => { if (e.key === 'Enter') handleSend(); };

window.onload = () => postToCSharp('get_history');
