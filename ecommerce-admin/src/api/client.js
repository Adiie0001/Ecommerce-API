// Thin client for the Ecommerce API.
//
// Token handling: the short-lived access token lives only in memory. The refresh token is kept in sessionStorage,
// so a page reload keeps the session but closing the tab ends it. sessionStorage is readable by scripts on the same
// origin, which is the usual trade-off for a browser-only admin console; keep this app free of third-party scripts.

const BASE_URL = (import.meta.env.VITE_API_URL ?? 'http://localhost:5000').replace(/\/$/, '')
const REFRESH_KEY = 'ecommerce-admin.refresh-token'

let accessToken = null
let refreshInFlight = null
let sessionExpiredHandler = () => {}

export class ApiError extends Error {
  constructor(status, problem) {
    super(problem?.detail || problem?.title || `Request failed (${status})`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem ?? null
    this.fieldErrors = problem?.errors ?? {}
    this.correlationId = problem?.correlationId ?? null
  }
}

export function setSessionExpiredHandler(handler) {
  sessionExpiredHandler = handler
}

function readRefreshToken() {
  try {
    return sessionStorage.getItem(REFRESH_KEY)
  } catch {
    return null
  }
}

function storeSession(session) {
  accessToken = session.accessToken
  try {
    sessionStorage.setItem(REFRESH_KEY, session.refreshToken)
  } catch {
    // Storage can be unavailable (private mode); the session then simply ends on reload.
  }
}

function clearSession() {
  accessToken = null
  try {
    sessionStorage.removeItem(REFRESH_KEY)
  } catch {
    // Nothing to clean up.
  }
}

function send(path, { method = 'GET', body, auth = true, signal } = {}) {
  const headers = { Accept: 'application/json' }
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  if (auth && accessToken) headers.Authorization = `Bearer ${accessToken}`

  return fetch(`${BASE_URL}${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  })
}

async function parse(response) {
  if (response.status === 204) return null

  const text = await response.text()
  let data = null
  if (text) {
    try {
      data = JSON.parse(text)
    } catch {
      data = { title: text.slice(0, 200) }
    }
  }

  if (!response.ok) throw new ApiError(response.status, data)
  return data
}

// One refresh at a time: parallel 401s share the same attempt instead of burning the single-use token twice.
function refreshSession() {
  const token = readRefreshToken()
  if (!token) return Promise.resolve(false)

  refreshInFlight ??= (async () => {
    const response = await send('/api/v1/auth/refresh', { method: 'POST', body: { refreshToken: token }, auth: false })
    if (!response.ok) return false
    storeSession(await response.json())
    return true
  })()
    .catch(() => false)
    .finally(() => {
      refreshInFlight = null
    })

  return refreshInFlight
}

export async function request(path, options = {}) {
  let response = await send(path, options)

  if (response.status === 401 && options.auth !== false) {
    if (await refreshSession()) {
      response = await send(path, options)
    }
    if (response.status === 401) {
      clearSession()
      sessionExpiredHandler()
    }
  }

  return parse(response)
}

export async function login(email, password) {
  const session = await request('/api/v1/auth/login', { method: 'POST', body: { email, password }, auth: false })
  storeSession(session)
  return session.user
}

export async function logout() {
  const token = readRefreshToken()
  clearSession()
  if (token) {
    try {
      await send('/api/v1/auth/logout', { method: 'POST', body: { refreshToken: token }, auth: false })
    } catch {
      // The local session is already gone; the server-side token also expires on its own.
    }
  }
}

/** Resumes a session after a page reload. Returns the user, or null when there is nothing to resume. */
export async function restoreSession() {
  if (!readRefreshToken() || !(await refreshSession())) {
    clearSession()
    return null
  }
  return request('/api/v1/auth/me')
}

export function listProducts({ page = 1, pageSize = 10, q = '', sort = 'name', order = 'asc' } = {}, signal) {
  const params = new URLSearchParams({ page, pageSize, sort, order })
  if (q.trim()) params.set('q', q.trim())
  return request(`/api/v1/products?${params}`, { signal })
}

export const getProduct = (id) => request(`/api/v1/products/${id}`)

export const createProduct = (product) => request('/api/v1/products', { method: 'POST', body: product })

export const updateProduct = (id, product) => request(`/api/v1/products/${id}`, { method: 'PUT', body: product })

export const deleteProduct = (id) => request(`/api/v1/products/${id}`, { method: 'DELETE' })

export const getStats = (signal) => request('/api/v1/products/stats', { signal })

export const recommend = (query) => request(`/api/v1/products/recommend?query=${encodeURIComponent(query)}`)

export async function checkReadiness() {
  try {
    const response = await fetch(`${BASE_URL}/health/ready`)
    return response.ok
  } catch {
    return false
  }
}
