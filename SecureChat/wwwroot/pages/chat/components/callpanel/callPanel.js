const callPanel = new class CallPanel {
    messagesContainer = document.getElementById('messages-container');
    callStartBtn = document.getElementById('call-start-btn');
    micBtn = document.getElementById('mic-btn');
    settingsBtn = document.getElementById('settings-btn');
    hangupBtn = document.getElementById('hangup-btn');
    callPanel = document.getElementById('call-panel');

    actions = {
        set_mic_state: d => this.setMicState(d.value),
        set_mic_volume: d => this.setMicVolume(d.value), // Новое действие
        sync_participants: d => this.syncParticipants(d.participants),
    };

    constructor() {
        // Слушаем ответ от C#
        window.chrome.webview.addEventListener('message', event => this.actions[event.data.action]?.(event.data));

        this.hangupBtn.onclick = () => {
            this.toggleCallUI(false);
            this.postToCSharp('leave_call');
        };

        // Открытие модалки
        this.settingsBtn.onclick = () => {
            this.postToCSharp('open_settings');
        };

        // Начать звонок (показать панель)
        this.callStartBtn.onclick = () => {
            this.toggleCallUI(true);
            this.postToCSharp('start_call');
        };

        // Обработка кнопок управления
        this.micBtn.onclick = () => {
            this.postToCSharp('toggle_mic', { active: !this.micBtn.classList.contains('active') });
        };
    }

    postToCSharp = (action, data = {}) => {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage({ action, ...data });
        }
    }

    // Генерация стабильного цвета на основе строки (имени)
    stringToColor = (str) => {
        let hash = 0;
        for (let i = 0; i < str.length; i++) {
            hash = str.charCodeAt(i) + ((hash << 5) - hash);
        }
        const c = (hash & 0x00FFFFFF).toString(16).toUpperCase();
        return "#" + "00000".substring(0, 6 - c.length) + c;
    }

    // Получение инициалов (первые две буквы)
    getInitials = (name) => {
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

    syncParticipants = (participants) => {
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
                const bgColor = this.stringToColor(p.Name);
                const initials = this.getInitials(p.Name);

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

    setMicVolume = (volume) => {
        if (!this.micBtn.classList.contains('active')) return;

        const percentage = Math.round(volume * 100);
        this.micBtn.style.background = `linear-gradient(to top, rgba(40, 167, 69, 0.8) ${percentage}%, transparent ${percentage}%)`;
    }

    setMicState = (state) => {
        // toggle вторым аргументом принимает boolean: true - добавить, false - удалить
        this.micBtn.classList.toggle('active', state);

        // Если выключили — сразу сбрасываем фон
        if (!state) this.micBtn.style.background = 'transparent';
    }

    toggleCallUI = (show) => {
        if (show) {
            // Удаляем d-none (Bootstrap) и скрытый (если был)
            this.callPanel.classList.remove('d-none', 'hidden');
        } else {
            // Добавляем стандартный класс скрытия Bootstrap
            this.callPanel.classList.add('d-none');
        }

        // Прокрутка чата после изменения высоты интерфейса
        setTimeout(() => {
            this.messagesContainer.scrollTo({
                top: this.messagesContainer.scrollHeight,
                behavior: 'smooth'
            });
        }, 150);
    }

    specialAction = (actionType) => {
        this.postToCSharp(actionType, {});
    }
}()