const chatPanel = new class ChatPanel {
    messagesContainer = document.getElementById('messages-container');
    messageInput = document.getElementById('message-input');
    sendBtn = document.getElementById('send-btn');
    attachBtn = document.getElementById('attach-btn');
    backToMainBtn = document.getElementById('back-to-main-btn');

    currentAttachment = { data: null, type: null, name: null };

    ICONS = {
        image: '<svg class="me-2" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect><circle cx="8.5" cy="8.5" r="1.5"></circle><polyline points="21 15 16 10 5 21"></polyline></svg>',
        file: '<svg class="me-2" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M13 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"></path><polyline points="13 2 13 9 20 9"></polyline></svg>'
    };

    actions = {
        append_message: d => this.appendMessage(d.role, d.text, d.id, d.status, d.senderName, d.imageUrl),
        update_message_status: d => this.updateMessageStatus(d.id, d.status),
        set_attachment: d => this.setAttachment(d.base64Data, d.fileName, d.isImage)
    };

    constructor() {
        // Слушаем ответ от C#
        window.chrome.webview.addEventListener('message', event => this.actions[event.data.action]?.(event.data));

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
                        this.setAttachment(e.target.result, "Изображение", true);
                    };

                    reader.readAsDataURL(blob);

                    // Если вставили картинку, обычно текст вставлять не нужно
                    // event.preventDefault(); 
                }
            }
        });

        this.backToMainBtn.addEventListener('click', () => {
            window.location.href = "https://app.localhost/pages/main/index.html";
        });

        this.sendBtn.onclick = () => {
            const text = this.messageInput.value.trim();
            // Разрешаем отправку, если есть либо текст, либо вложение
            if (text || this.currentAttachment.data) {
                const tempId = 'msg_' + Date.now();

                // Передаем весь объект вложения
                this.appendMessage('user', text, tempId, 'pending', 'Вы', this.currentAttachment);

                this.postToCSharp('send_message', {
                    text: text,
                    id: tempId,
                    attachment: this.currentAttachment // Отправляем объект {data, type, name}
                });

                this.messageInput.value = '';
                this.clearAttachment();
            }
        };

        this.messageInput.addEventListener('keydown', (event) => {
            if (event.key === 'Enter') {
                event.preventDefault(); // Чтобы страница не перезагружалась, если есть форма
                this.sendBtn.click(); // Симулируем нажатие кнопки
            }
        });

        this.attachBtn.onclick = () => {
            // Отправляем сообщение в C# через PostMessage
            // Если ваш IWebView поддерживает PostMessage:
            this.postToCSharp('open_file_dialog');
        }
    }

    postToCSharp(action, data = {}) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage({ action, ...data });
        }
    }

    appendMessage(role, text, id = null, status = 'sent', senderName = 'Бот', attachment = null) {
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
                ${this.ICONS.file}
                <small class="text-truncate" style="max-width: 150px;">${attachment.name}</small>
            `;
                // Опционально: клик по файлу для скачивания/открытия
                fileBox.onclick = () => {
                    this.postToCSharp("try_open_loaded_file", { fileName: attachment.name });
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

        this.messagesContainer.appendChild(fragment);
        this.messagesContainer.scrollTo({ top: this.messagesContainer.scrollHeight, behavior: 'smooth' });
    }

    // Универсальная функция прикрепления
    setAttachment(base64Data, fileName, isImage) {
        this.currentAttachment = { data: base64Data, type: isImage ? 'image' : 'file', name: fileName };

        const container = document.getElementById('attachment-preview');
        const label = document.getElementById('attachment-label');
        const badge = document.querySelector('.image-badge');

        // Меняем иконку и текст (обрезаем длинные имена файлов)
        const displayName = fileName.length > 20 ? fileName.substring(0, 17) + '...' : fileName;
        label.innerHTML = (isImage ? this.ICONS.image : this.ICONS.file) + displayName;

        // Стилизуем под файл, если это не картинка
        isImage ? badge.classList.remove('file-badge') : badge.classList.add('file-badge');

        container.classList.remove('d-none');
        setTimeout(() => container.style.opacity = "1", 10);
    }

    clearAttachment() {
        this.currentAttachment = { data: null, type: null, name: null };
        const container = document.getElementById('attachment-preview');
        container.style.opacity = "0";
        setTimeout(() => container.classList.add('d-none'), 200);
    }

    updateMessageStatus(id, status) {
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
}()