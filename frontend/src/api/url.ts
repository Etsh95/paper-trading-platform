const base = (import.meta.env.VITE_API_URL ?? '').replace(/\/+$/, '')

/** Empty base in development: requests go to the Vite dev server, which proxies /api and /hubs. */
export const apiUrl = (path: string) => `${base}${path}`
