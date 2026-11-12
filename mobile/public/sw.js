// Service worker mínimo: hace que Android ofrezca "Instalar app". No guarda nada en caché a propósito,
// así cada deploy llega enseguida y nunca se ven datos viejos (la app siempre necesita la API).
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));
self.addEventListener('fetch', () => {});
