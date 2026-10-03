const CACHE_NAME = 'naryadai-cache-v2';
const STATIC_ASSETS = [
    './offline.html',
    './manifest.json',
    './favicon.png',
    './_content/MudBlazor/MudBlazor.min.css',
    './_content/MudBlazor/MudBlazor.min.js'
];

self.addEventListener('install', event => {
    event.waitUntil(caches.open(CACHE_NAME).then(cache => cache.addAll(STATIC_ASSETS)));
    self.skipWaiting();
});

self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys().then(names => Promise.all(
            names
                .filter(name => name.startsWith('naryadai-cache-') && name !== CACHE_NAME)
                .map(name => caches.delete(name))
        ))
    );
    self.clients.claim();
});

self.addEventListener('fetch', event => {
    const request = event.request;
    if (request.method !== 'GET') return;

    const url = new URL(request.url);
    if (url.origin !== self.location.origin) return;

    if (request.mode === 'navigate') {
        event.respondWith(
            fetch(request).catch(async () =>
                (await caches.match('./offline.html')) ?? new Response('Нет подключения к сети.', {
                    status: 503,
                    headers: { 'Content-Type': 'text/plain; charset=utf-8' }
                })
            )
        );
        return;
    }

    const isStaticAsset = /\.(?:css|js|png|svg|woff2?|ttf|webp|jpe?g|ico)$/i.test(url.pathname)
        && !url.pathname.includes('/App_Data/');
    if (!isStaticAsset) return;

    event.respondWith((async () => {
        const cached = await caches.match(request);
        if (cached) return cached;

        const response = await fetch(request);
        const cacheControl = response.headers.get('Cache-Control') ?? '';
        if (response.ok && response.type === 'basic' && !/private|no-store/i.test(cacheControl)) {
            const cache = await caches.open(CACHE_NAME);
            await cache.put(request, response.clone());
        }
        return response;
    })());
});
