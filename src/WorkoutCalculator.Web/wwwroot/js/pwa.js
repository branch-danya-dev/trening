// Установка как приложения и офлайн-режим: регистрирует service worker. Когда вышла новая версия и уже
// скачалась, предлагает сохранить работу и закрыть все вкладки. Активный черновик не перезагружается.
if ('serviceWorker' in navigator) {
    // Never reload a live draft/transaction in response to another tab's update.

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
    text.textContent = 'Вышла новая версия. Сохраните работу, закройте все вкладки приложения и откройте его снова.';
    banner.setAttribute('data-testid','pwa-update-ready');
    banner.append(text);
    document.body.append(banner);
}
