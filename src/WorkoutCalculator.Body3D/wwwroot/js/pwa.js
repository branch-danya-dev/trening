// Установка как приложения и офлайн-режим: регистрирует service worker. Когда вышла новая версия и уже
// скачалась, показывает плашку «Обновить» — без неё новая версия включилась бы только после закрытия всех вкладок.
if ('serviceWorker' in navigator) {
    let reloading = false;
    navigator.serviceWorker.addEventListener('controllerchange', () => {
        if (reloading) return;
        reloading = true;
        location.reload();
    });

    navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' })
        .then(registration => {
            if (registration.waiting && navigator.serviceWorker.controller) offerUpdate(registration.waiting);
            registration.addEventListener('updatefound', () => {
                const worker = registration.installing;
                worker?.addEventListener('statechange', () => {
                    if (worker.state === 'installed' && navigator.serviceWorker.controller) offerUpdate(worker);
                });
            });
        })
        .catch(() => { /* без офлайн-режима приложение всё равно работает */ });
}

function offerUpdate(worker) {
    if (document.querySelector('.update-banner')) return;
    const banner = document.createElement('div');
    banner.className = 'update-banner';
    banner.setAttribute('role', 'status');
    const text = document.createElement('span');
    text.textContent = 'Вышла новая версия';
    const button = document.createElement('button');
    button.className = 'btn';
    button.textContent = 'Обновить';
    button.addEventListener('click', () => worker.postMessage('skip-waiting'));
    banner.append(text, button);
    document.body.append(banner);
}
