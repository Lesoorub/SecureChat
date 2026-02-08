const messagesContainer = document.getElementById('messages-container');
const messageInput = document.getElementById('message-input');
const sendBtn = document.getElementById('send-btn');

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

sendBtn.onclick = handleSend;
messageInput.onkeypress = (e) => { if (e.key === 'Enter') handleSend(); };

window.onload = () => postToCSharp('get_history');
