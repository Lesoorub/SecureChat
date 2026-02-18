const messagesContainer = document.getElementById('messages-container');
const messageInput = document.getElementById('message-input');
const sendBtn = document.getElementById('send-btn');
const callPanel = document.getElementById('call-panel');
const callStartBtn = document.getElementById('call-start-btn');
const micBtn = document.getElementById('mic-btn');
const settingsBtn = document.getElementById('settings-btn');
const hangupBtn = document.getElementById('hangup-btn');
const attachBtn = document.getElementById('attach-btn');

let currentAttachment = { data: null, type: null, name: null };

const ICONS = {
    image: '<svg class="me-2" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect><circle cx="8.5" cy="8.5" r="1.5"></circle><polyline points="21 15 16 10 5 21"></polyline></svg>',
    file: '<svg class="me-2" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M13 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"></path><polyline points="13 2 13 9 20 9"></polyline></svg>'
};

function postToCSharp(action, data = {}) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage({ action, ...data });
    }
}
const actions = {
    set_mic_state: d => setMicState(d.value),
    append_message: d => appendMessage(d.role, d.text, d.id, d.status, d.senderName, d.imageUrl),
    update_message_status: d => updateMessageStatus(d.id, d.status),
    sync_participants: d => syncParticipants(d.participants),
    set_attachment: d => setAttachment(d.base64Data, d.fileName, d.isImage)
};

// Слушаем ответ от C#
window.chrome.webview.addEventListener('message', event => {
    const data = event.data;

    actions[data.action]?.(data);
});

window.addEventListener('paste', async (event) => {
    const items = (event.clipboardData || event.originalEvent.clipboardData).items;

    for (const item of items) {
        // Проверяем, является ли вставленный объект изображением
        if (item.type.indexOf('image') !== -1) {
            const blob = item.getAsFile();
            const reader = new FileReader();

            reader.onload = (e) => {
                // Используем новую универсальную функцию
                // Параметры: base64, имя отображения, флаг "это картинка"
                setAttachment(e.target.result, "Изображение", true);
            };

            reader.readAsDataURL(blob);

            // Если вставили картинку, обычно текст вставлять не нужно
            // event.preventDefault(); 
        }
    }
});

function appendMessage(role, text, id = null, status = 'sent', senderName = 'Бот', attachment = null)
{
    const template = document.getElementById('message-template');
    const fragment = template.content.cloneNode(true);

    const wrapper = fragment.querySelector('.message-wrapper');
    const msgDiv = fragment.querySelector('.message');
    const textSpan = fragment.querySelector('.text-content');
    const nameDiv = fragment.querySelector('.sender-name');
    const statusSpan = fragment.querySelector('.status');

    msgDiv.id = id || 'msg_' + Date.now();
    textSpan.textContent = text;

    // --- Логика вложений ---
    if (attachment && attachment.data) {
        if (attachment.type === 'image') {
            // Отрисовка картинки
            const img = document.createElement('img');
            img.src = attachment.data;
            img.classList.add('img-fluid', 'rounded-3', 'mb-2');
            img.style.maxHeight = '300px';
            img.style.objectFit = 'contain';
            img.style.display = 'block';
            msgDiv.insertBefore(img, textSpan);
        } else {
            // Отрисовка файла (плашка с иконкой)
            const fileBox = document.createElement('div');
            fileBox.className = 'd-flex align-items-center p-2 mb-2 rounded bg-black bg-opacity-10 border border-white border-opacity-25';
            fileBox.style.cursor = 'pointer';
            fileBox.innerHTML = `
                ${ICONS.file}
                <small class="text-truncate" style="max-width: 150px;">${attachment.name}</small>
            `;
            // Опционально: клик по файлу для скачивания/открытия
            fileBox.onclick = () =>
            {
                postToCSharp("try_open_loaded_file", { fileName: attachment.name });
            };
            msgDiv.insertBefore(fileBox, textSpan);
        }
    }

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
}

// Универсальная функция прикрепления
function setAttachment(base64Data, fileName, isImage) {
    currentAttachment = { data: base64Data, type: isImage ? 'image' : 'file', name: fileName };

    const container = document.getElementById('attachment-preview');
    const label = document.getElementById('attachment-label');
    const badge = document.querySelector('.image-badge');

    // Меняем иконку и текст (обрезаем длинные имена файлов)
    const displayName = fileName.length > 20 ? fileName.substring(0, 17) + '...' : fileName;
    label.innerHTML = (isImage ? ICONS.image : ICONS.file) + displayName;

    // Стилизуем под файл, если это не картинка
    isImage ? badge.classList.remove('file-badge') : badge.classList.add('file-badge');

    container.classList.remove('d-none');
    setTimeout(() => container.style.opacity = "1", 10);
}

function clearAttachment() {
    currentAttachment = { data: null, type: null, name: null };
    const container = document.getElementById('attachment-preview');
    container.style.opacity = "0";
    setTimeout(() => container.classList.add('d-none'), 200);
}

function updateMessageStatus(id, status)
{
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
}

document.getElementById('back-to-main-btn').addEventListener('click', () => {
    window.location.href = "https://app.localhost/pages/main/index.html";
});

sendBtn.onclick = function () {
    const text = messageInput.value.trim();
    // Разрешаем отправку, если есть либо текст, либо вложение
    if (text || currentAttachment.data) {
        const tempId = 'msg_' + Date.now();

        // Передаем весь объект вложения
        appendMessage('user', text, tempId, 'pending', 'Вы', currentAttachment);

        postToCSharp('send_message', {
            text: text,
            id: tempId,
            attachment: currentAttachment // Отправляем объект {data, type, name}
        });

        messageInput.value = '';
        clearAttachment();
    }
};

// Обработка кнопок управления
micBtn.onclick = function () {
    postToCSharp('toggle_mic', { active: !this.classList.contains('active') });
};

hangupBtn.onclick = () => {
    toggleCallUI(false);
    postToCSharp('leave_call');
};

// Открытие модалки
settingsBtn.onclick = () => {
    postToCSharp('open_settings');
};

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

function syncParticipants(participants) {
    const list = document.getElementById('participants-list');
    const currentIds = new Set(participants.map(p => `user-${p.Id}`));

    // 1. Удаляем тех, кто отключился
    Array.from(list.children).forEach(el => {
        if (!currentIds.has(el.id)) el.remove();
    });

    // 2. Добавляем новых или обновляем существующих
    participants.forEach(p => {
        let el = document.getElementById(`user-${p.Id}`);

        if (!el) {
            // Создаем элемент из шаблона, если его еще нет
            const template = document.getElementById('participant-template');
            const fragment = template.content.cloneNode(true);

            // Находим корневой контейнер внутри фрагмента
            el = fragment.querySelector('.participant');
            el.id = `user-${p.Id}`;

            const avatar = el.querySelector('.avatar');
            const nameLabel = el.querySelector('.participant-name');

            // --- Ваша логика отрисовки (как в updateParticipants) ---
            const bgColor = stringToColor(p.Name);
            const initials = getInitials(p.Name);

            // Убираем мешающий класс Bootstrap
            avatar.classList.remove('bg-secondary');

            avatar.style.backgroundColor = bgColor;
            avatar.style.color = '#fff';
            avatar.textContent = initials;
            nameLabel.textContent = p.Name;

            list.appendChild(el);
        }

        // 3. Обновляем статус индикации голоса (без пересоздания элемента)
        if (p.IsSpeaking) {
            el.classList.add('speaking');
        } else {
            el.classList.remove('speaking');
        }
    });
}

function specialAction(actionType) {
    postToCSharp(actionType, {});
}

function setMicState(state)
{
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

messageInput.addEventListener('keydown', function (event) {
    if (event.key === 'Enter') {
        event.preventDefault(); // Чтобы страница не перезагружалась, если есть форма
        sendBtn.click(); // Симулируем нажатие кнопки
    }
});

attachBtn.onclick = function handleAttachClick() {
    // Отправляем сообщение в C# через PostMessage
    // Если ваш IWebView поддерживает PostMessage:
    postToCSharp('open_file_dialog');
}

window.onload = () => postToCSharp('get_history');
