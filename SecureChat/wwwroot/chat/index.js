const messagesContainer = document.getElementById('messages-container');
const messageInput = document.getElementById('message-input');
const sendBtn = document.getElementById('send-btn');
const callPanel = document.getElementById('call-panel');
const callStartBtn = document.getElementById('call-start-btn');
const micBtn = document.getElementById('mic-btn');

function postToCSharp(action, data = {}) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage({ action, ...data });
    }
}

window.appendMessage = (role, text, id = null, status = 'sent', senderName = 'Бот') => {
    const template = document.getElementById('message-template');
    const fragment = template.content.cloneNode(true);

    const wrapper = fragment.querySelector('.message-wrapper');
    const msgDiv = fragment.querySelector('.message');
    const textSpan = fragment.querySelector('.text-content');
    const nameDiv = fragment.querySelector('.sender-name');
    const statusSpan = fragment.querySelector('.status');

    msgDiv.id = id || 'msg_' + Date.now();
    textSpan.textContent = text;

    if (role === 'system') {
        // Системное: по центру, жирное, без оформления
        wrapper.classList.add('align-items-center');
        msgDiv.classList.add('fw-bold', 'text-muted', 'small', 'text-center');
        nameDiv.remove();
        statusSpan.remove();
    } else if (role === 'user') {
        // Пользователь: справа, синий фон, тень
        wrapper.classList.add('align-items-end');
        // Оставляем bg-primary для обычных сообщений
        msgDiv.classList.add('bg-primary', 'text-white', 'rounded-4', 'rounded-bottom-end-0', 'shadow-sm', 'border');

        nameDiv.remove();
        if (status === 'pending') {
            msgDiv.classList.add('pending-anim');
        } else if (status === 'error') {
            msgDiv.classList.add('error-bg');
        }
    } else {
        // Бот: слева, светлый фон, тень
        wrapper.classList.add('align-items-start');
        msgDiv.classList.add('bg-light', 'text-dark', 'rounded-4', 'rounded-bottom-start-0', 'shadow-sm', 'border');
        nameDiv.textContent = senderName;
        statusSpan.remove();
    }

    messagesContainer.appendChild(fragment);
    messagesContainer.scrollTo({ top: messagesContainer.scrollHeight, behavior: 'smooth' });
};

window.updateMessageStatus = (id, status) => {
    const msg = document.getElementById(id);
    if (!msg) return;

    // Сначала очищаем все спец-состояния
    msg.classList.remove('pending-anim', 'error-bg');

    if (status === 'pending') {
        msg.classList.add('pending-anim');
    }
    else if (status === 'error') {
        msg.classList.add('error-bg');
    }
    // Если status === 'sent', мы просто оставили классы пустыми, 
    // и Bootstrap вернет стандартный bg-primary
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
        const fragment = template.content.cloneNode(true);
        const container = fragment.querySelector('.participant');
        const avatar = fragment.querySelector('.avatar');
        const nameLabel = fragment.querySelector('.participant-name');

        container.id = `user-${p.id}`;
        if (p.isSpeaking) container.classList.add('speaking');

        // Генерируем цвет и инициалы
        const bgColor = stringToColor(p.name);
        const initials = getInitials(p.name);

        // ВАЖНО: Удаляем стандартный серый класс Bootstrap, чтобы работал inline-style
        avatar.classList.remove('bg-secondary');

        // Устанавливаем цвет фона и текст
        avatar.style.backgroundColor = bgColor;
        avatar.style.color = '#fff'; // Всегда белый текст для контраста
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

window.setUsersSpeaking = (states) => {
    Object.entries(states).forEach(([userId, isSpeaking]) => {
        const el = document.getElementById(`user-${userId}`);
        if (el) {
            el.classList.toggle('speaking', isSpeaking);
        }
    });
};

// Обработка кнопок управления
micBtn.onclick = function () {
    postToCSharp('toggle_mic', { active: !this.classList.contains('active') });
};

document.getElementById('hangup-btn').onclick = () => {
    toggleCallUI(false);
    postToCSharp('leave_call');
};

window.setMicState = (state) => {
    micBtn.classList.remove('active');
    if (state) {
        micBtn.classList.add('active');
    }
}

const toggleCallUI = (show) => {
    if (show) {
        // Удаляем d-none (Bootstrap) и скрытый (если был)
        callPanel.classList.remove('d-none', 'hidden');
    } else {
        // Добавляем стандартный класс скрытия Bootstrap
        callPanel.classList.add('d-none');
    }

    // Прокрутка чата после изменения высоты интерфейса
    setTimeout(() => {
        messagesContainer.scrollTo({
            top: messagesContainer.scrollHeight,
            behavior: 'smooth'
        });
    }, 150);
};

sendBtn.onclick = handleSend;
messageInput.onkeypress = (e) => { if (e.key === 'Enter') handleSend(); };

window.onload = () => postToCSharp('get_history');
