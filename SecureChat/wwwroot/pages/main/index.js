const
    userInput = document.getElementById("user-name"),
    roomInput = document.getElementById("room-name"),
    passInput = document.getElementById("room-password"),
    errorDisplay = document.getElementById("error-display"),
    btnCreate = document.getElementById("btn-create"),
    btnJoin = document.getElementById("btn-join"),
    settingsModal = document.getElementById("settings-modal"),
    serverInput = document.getElementById("server-url-input");

function postToCSharp(action, data = {}) {
    window.chrome?.webview && window.chrome.webview.postMessage({ action, ...data });
}

// Валидация и сбор данных
function getAuthData() {
    const user = userInput.value.trim();
    const room = roomInput.value.trim();
    const pass = passInput.value.trim();

    if (!user || !room || !pass) {
        window.api.processError("Заполните все поля");
        return null;
    }
    errorDisplay.classList.add("hidden");
    return { user, room, pass }; // Данные для C#
}

// Обработка кнопок
btnCreate.onclick = () => {
    const data = getAuthData();
    if (data) {
        btnCreate.disabled = true;
        btnCreate.textContent = "Создание...";
        postToCSharp("CREATE_ROOM", data);
    }
};

btnJoin.onclick = () => {
    const data = getAuthData();
    if (data) {
        btnJoin.disabled = true;
        btnJoin.textContent = "Вход...";
        postToCSharp("JOIN_ROOM", data);
    }
};

// API для вызова из C#
window.api = {
    processError: (msg) => {
        errorDisplay.textContent = msg;
        errorDisplay.classList.remove("hidden");
        btnCreate.disabled = false;
        btnCreate.textContent = "Создать комнату";
        btnJoin.disabled = false;
        btnJoin.textContent = "Войти в чат";
    },
    processSuccess: () => {
        window.location.href = "https://app.localhost/pages/chat/index.html";
    },
    initSettings: (url) => {
        serverInput.value = url;
    },
    setVersion: (serverVersion) => {
        const currentVersion = "1.0.0"; // Версия текущей сборки клиента
        document.getElementById("app-version").textContent = "v" + currentVersion;

        if (serverVersion !== currentVersion) {
            document.getElementById("update-link").classList.remove("hidden");
        }
    }
};

document.getElementById("btn-download").onclick = (e) => {
    e.preventDefault();
    postToCSharp("DOWNLOAD_UPDATE");
};

// Настройки
document.getElementById("btn-settings-open").onclick = () => {
    settingsModal.classList.remove("hidden");
    serverInput.focus();
};
document.getElementById("btn-settings-close").onclick = () => settingsModal.classList.add("hidden");
document.getElementById("btn-settings-save").onclick = () => {
    postToCSharp("SAVE_SETTINGS", { serverUrl: serverInput.value });
    settingsModal.classList.add("hidden");
};

// Очистка ошибок при вводе
[roomInput, passInput].forEach(el => el.oninput = () => errorDisplay.classList.add("hidden"));