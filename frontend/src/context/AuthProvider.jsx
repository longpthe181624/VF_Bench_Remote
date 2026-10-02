import { useEffect, useState, useSyncExternalStore } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { sessionStore } from '../services/session'
import { api } from '../services/api'
import { AuthContext } from './auth-context'
export function AuthProvider({ children }) {
  const session = useSyncExternalStore(sessionStore.subscribe, sessionStore.get)
  const [ready, setReady] = useState(false)
  const cache = useQueryClient()
  useEffect(() => {
    let active = true
    const saved = sessionStore.get()
    const check = saved ? api.get('/auth/me').then(({ data }) => {
      if (active && sessionStore.get()) sessionStore.set({ ...sessionStore.get(), nguoiDung: data })
    }).catch(() => { if (active) sessionStore.set(null) }) : Promise.resolve()
    check.finally(() => { if (active) setReady(true) })
    return () => { active = false }
  }, [])
  useEffect(() => { if (!session) { cache.cancelQueries(); cache.clear() } }, [session, cache])
  const accept = value => { cache.clear(); sessionStore.set(value) }
  const logout = () => { cache.cancelQueries(); cache.clear(); sessionStore.set(null) }
  const can = permission => !!session && (session.nguoiDung.vaiTro.includes('Admin') || session.nguoiDung.quyen.includes(permission))
  return <AuthContext.Provider value={{ session, user: session?.nguoiDung, ready, accept, logout, can }}>{children}</AuthContext.Provider>
}
