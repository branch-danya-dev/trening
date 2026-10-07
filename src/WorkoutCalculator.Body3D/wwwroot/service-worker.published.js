// Офлайн-режим опубликованной версии: при установке кладёт в кэш все файлы приложения из списка сборки
// (service-worker-assets.js, с проверкой целостности) и дальше отдаёт их из кэша. Новая версия ставится
// в фоне и включается, когда пользователь нажмёт «Обновить» (js/pwa.js) или закроет все вкладки.
self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall()));
self.addEventListener('activate', event => event.waitUntil(onActivate()));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));
self.addEventListener('message', event => {
    if (event.data === 'skip-waiting') self.skipWaiting();
});

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [/\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff2?$/,
    /\.png$/, /\.jpe?g$/, /\.svg$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.bin$/, /\.webmanifest$/];
const offlineAssetsExclude = [/^service-worker\.js$/];

// Адреса — от папки, где лежит сам service worker: на GitHub Pages это /trening/, локально — корень
const baseUrl = new URL('./', self.location.href);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall() {
    const requests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(new URL(asset.url, baseUrl), { integrity: asset.hash, cache: 'no-cache' }));
    const cache = await caches.open(cacheName);
    await cache.addAll(requests);
}

async function onActivate() {
    const keys = await caches.keys();
    await Promise.all(keys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    if (event.request.method !== 'GET') return fetch(event.request);
    // Переход по адресу приложения — всегда index.html из кэша: работает и без сети
    const isNavigation = event.request.mode === 'navigate' && !manifestUrlList.includes(event.request.url);
    const request = isNavigation ? new URL('index.html', baseUrl).href : event.request;
    const cache = await caches.open(cacheName);
    // ignoreSearch: данные MakeHuman запрашиваются с ?v=<версия формата>, а в кэше лежат без него
    const cached = await cache.match(request, { ignoreSearch: true });
    return cached || fetch(event.request);
}
