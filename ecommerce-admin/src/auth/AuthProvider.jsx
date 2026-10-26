import { useCallback, useEffect, useMemo, useState } from 'react'
import * as api from '../api/client'
import { AuthContext } from './context'

const ADMIN_ONLY_MESSAGE = 'This console is for administrators. Your account does not have access.'

export function AuthProvider({ children }) {
  const [status, setStatus] = useState('checking')
  const [user, setUser] = useState(null)

  useEffect(() => {
    api.setSessionExpiredHandler(() => {
      setUser(null)
      setStatus('anonymous')
    })

    let cancelled = false
    api
      .restoreSession()
      .then((restored) => {
        if (cancelled) return
        if (restored?.role === 'Admin') {
          setUser(restored)
          setStatus('authenticated')
        } else {
          setStatus('anonymous')
        }
      })
      .catch(() => {
        if (!cancelled) setStatus('anonymous')
      })

    return () => {
      cancelled = true
    }
  }, [])

  const login = useCallback(async (email, password) => {
    const signedIn = await api.login(email, password)
    if (signedIn.role !== 'Admin') {
      await api.logout()
      throw new Error(ADMIN_ONLY_MESSAGE)
    }
    setUser(signedIn)
    setStatus('authenticated')
  }, [])

  const logout = useCallback(async () => {
    await api.logout()
    setUser(null)
    setStatus('anonymous')
  }, [])

  const value = useMemo(() => ({ status, user, login, logout }), [status, user, login, logout])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
