// Офлайн-режим опубликованной версии: при установке кладёт в кэш все файлы приложения из списка сборки
// (service-worker-assets.js, с проверкой целостности) и дальше отдаёт их из кэша. Новая версия ставится
// в фоне и включается после закрытия всех вкладок. Принудительная активация может смешать версии и потерять черновик.
self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall()));
self.addEventListener('activate', event => event.waitUntil(onActivate()));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));
// Deliberately no skipWaiting / clients.claim: an old tab keeps its complete old bundle until closed.

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [/\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff2?$/,
    /\.png$/, /\.jpe?g$/, /\.svg$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.bin$/, /\.webmanifest$/];
const offlineAssetsExclude = [/^service-worker\.js$/, /^lib\/mediapipe-/];

// Распознавание позы (~22 МБ) нужно только для съёмки: в кэш оно кладётся при первом использовании,
// а не при установке. Папка с версией в имени — обновление MediaPipe не перепутает файлы.
const runtimeCacheName = 'runtime-mediapipe';
const isRuntimeAsset = url => url.startsWith(new URL('lib/mediapipe-', baseUrl).href);

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
    // Файлы MediaPipe, которых нет в этой версии приложения (старая версия библиотеки), — удалить
    const runtime = await caches.open(runtimeCacheName);
    for (const request of await runtime.keys()) {
        if (!manifestUrlList.includes(request.url)) await runtime.delete(request);
    }
}

async function onFetch(event) {
    if (event.request.method !== 'GET') return fetch(event.request);
    if (isRuntimeAsset(event.request.url)) {
        const runtime = await caches.open(runtimeCacheName);
        const hit = await runtime.match(event.request.url);
        if (hit) return hit;
        const response = await fetch(event.request);
        if (response.ok) await runtime.put(event.request.url, response.clone());
        return response;
    }
    // Переход по адресу приложения — всегда index.html из кэша: работает и без сети
    const isNavigation = event.request.mode === 'navigate' && !manifestUrlList.includes(event.request.url);
    const request = isNavigation ? new URL('index.html', baseUrl).href : event.request;
    const cache = await caches.open(cacheName);
    // ignoreSearch: данные MakeHuman запрашиваются с ?v=<версия формата>, а в кэше лежат без него
    const cached = await cache.match(request, { ignoreSearch: true });
    return cached || fetch(event.request);
}
