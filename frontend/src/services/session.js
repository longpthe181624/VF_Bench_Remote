const key = 'benchconsole.phien'
const listeners = new Set()
let session = null
try { const saved = JSON.parse(localStorage.getItem(key)); if (saved?.accessToken && saved?.nguoiDung?.id) session = saved } catch { localStorage.removeItem(key) }
export const sessionStore = {
  get: () => session,
  subscribe(listener) { listeners.add(listener); return () => listeners.delete(listener) },
  set(value) {
    session = value
    if (value) localStorage.setItem(key, JSON.stringify(value)); else localStorage.removeItem(key)
    listeners.forEach(listener => listener())
  },
}
