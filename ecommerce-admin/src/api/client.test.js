import { beforeEach, describe, expect, it, vi } from 'vitest'
import { admin, jsonResponse, mockApi, page, problem, product, session } from '../test/mockApi'

// The client keeps the access token in module state, so every test loads a fresh copy.
async function loadClient() {
  vi.resetModules()
  return import('./client')
}

describe('api client', () => {
  beforeEach(() => {
    sessionStorage.clear()
  })

  it('sends the access token after login and keeps the refresh token in sessionStorage', async () => {
    const api = mockApi({
      'POST /api/v1/auth/login': session(),
      'GET /api/v1/products': page([product()]),
    })
    const client = await loadClient()

    const user = await client.login('admin@shop.test', 'pw')
    await client.listProducts()

    expect(user).toEqual(admin)
    expect(api.callsTo('POST /api/v1/auth/login')[0].headers.Authorization).toBeUndefined()
    expect(api.callsTo('GET /api/v1/products')[0].headers.Authorization).toBe('Bearer access-1')
    expect(sessionStorage.getItem('ecommerce-admin.refresh-token')).toBe('refresh-1')
  })

  it('refreshes once on a 401, stores the new pair and retries the original request', async () => {
    let attempts = 0
    const api = mockApi({
      'POST /api/v1/auth/login': session(),
      'POST /api/v1/auth/refresh': session(admin, 2),
      'GET /api/v1/products': () => (++attempts === 1 ? jsonResponse(401, problem(401)) : page([product()])),
    })
    const client = await loadClient()
    await client.login('admin@shop.test', 'pw')

    const result = await client.listProducts()

    expect(result.items).toHaveLength(1)
    expect(api.callsTo('POST /api/v1/auth/refresh')[0].body).toEqual({ refreshToken: 'refresh-1' })
    expect(api.callsTo('GET /api/v1/products')[1].headers.Authorization).toBe('Bearer access-2')
    expect(sessionStorage.getItem('ecommerce-admin.refresh-token')).toBe('refresh-2')
  })

  it('lets parallel 401s share one refresh, because the refresh token works only once', async () => {
    let refreshed = false
    const api = mockApi({
      'POST /api/v1/auth/login': session(),
      'POST /api/v1/auth/refresh': () => {
        refreshed = true
        return session(admin, 2)
      },
      'GET /api/v1/products': (call) =>
        call.headers.Authorization === 'Bearer access-2' ? page([product()]) : jsonResponse(401, problem(401)),
      'GET /api/v1/products/stats': (call) =>
        call.headers.Authorization === 'Bearer access-2' ? { totalProducts: 1 } : jsonResponse(401, problem(401)),
    })
    const client = await loadClient()
    await client.login('admin@shop.test', 'pw')

    await Promise.all([client.listProducts(), client.getStats()])

    expect(refreshed).toBe(true)
    expect(api.callsTo('POST /api/v1/auth/refresh')).toHaveLength(1)
  })

  it('ends the session and notifies the app when the refresh fails', async () => {
    mockApi({
      'POST /api/v1/auth/login': session(),
      'POST /api/v1/auth/refresh': jsonResponse(401, problem(401)),
      'GET /api/v1/products': jsonResponse(401, problem(401)),
    })
    const client = await loadClient()
    const expired = vi.fn()
    client.setSessionExpiredHandler(expired)
    await client.login('admin@shop.test', 'pw')

    await expect(client.listProducts()).rejects.toMatchObject({ status: 401 })

    expect(expired).toHaveBeenCalledTimes(1)
    expect(sessionStorage.getItem('ecommerce-admin.refresh-token')).toBeNull()
  })

  it('does not try to refresh when the login itself is rejected', async () => {
    const api = mockApi({
      'POST /api/v1/auth/login': jsonResponse(401, problem(401, { detail: 'Invalid email or password.' })),
    })
    const client = await loadClient()

    await expect(client.login('admin@shop.test', 'wrong')).rejects.toThrow('Invalid email or password.')

    expect(api.callsTo('POST /api/v1/auth/refresh')).toHaveLength(0)
  })

  it('exposes field errors and the correlation id from problem details', async () => {
    mockApi({
      'POST /api/v1/products': jsonResponse(400, problem(400, { errors: { price: ['Price must be positive.'] } })),
    })
    const client = await loadClient()

    const error = await client.createProduct({ name: 'x' }).catch((e) => e)

    expect(error).toBeInstanceOf(client.ApiError)
    expect(error.status).toBe(400)
    expect(error.fieldErrors.price[0]).toBe('Price must be positive.')
    expect(error.correlationId).toBe('corr-1')
  })

  it('treats 204 as success with no body', async () => {
    mockApi({ 'DELETE /api/v1/products/5': new Response(null, { status: 204 }) })
    const client = await loadClient()

    await expect(client.deleteProduct(5)).resolves.toBeNull()
  })

  it('builds the listing query from the filters and skips an empty search', async () => {
    const api = mockApi({ 'GET /api/v1/products': page([]) })
    const client = await loadClient()

    await client.listProducts({ page: 3, pageSize: 10, q: '  mouse ', sort: 'price', order: 'desc' })
    await client.listProducts({ q: '   ' })

    const [first, second] = api.callsTo('GET /api/v1/products')
    expect(Object.fromEntries(first.params)).toEqual({ page: '3', pageSize: '10', sort: 'price', order: 'desc', q: 'mouse' })
    expect(second.params.has('q')).toBe(false)
  })

  it('resumes a session from the stored refresh token', async () => {
    mockApi({
      'POST /api/v1/auth/refresh': session(admin, 2),
      'GET /api/v1/auth/me': admin,
    })
    sessionStorage.setItem('ecommerce-admin.refresh-token', 'refresh-1')
    const client = await loadClient()

    await expect(client.restoreSession()).resolves.toEqual(admin)
  })

  it('has nothing to resume without a stored token, and drops a token the server rejects', async () => {
    const api = mockApi({ 'POST /api/v1/auth/refresh': jsonResponse(401, problem(401)) })
    const client = await loadClient()

    await expect(client.restoreSession()).resolves.toBeNull()
    expect(api.calls).toHaveLength(0)

    sessionStorage.setItem('ecommerce-admin.refresh-token', 'stale')
    await expect(client.restoreSession()).resolves.toBeNull()
    expect(sessionStorage.getItem('ecommerce-admin.refresh-token')).toBeNull()
  })

  it('logs out locally even when the server cannot be reached', async () => {
    mockApi({
      'POST /api/v1/auth/login': session(),
      'POST /api/v1/auth/logout': () => {
        throw new TypeError('network down')
      },
    })
    const client = await loadClient()
    await client.login('admin@shop.test', 'pw')

    await expect(client.logout()).resolves.toBeUndefined()

    expect(sessionStorage.getItem('ecommerce-admin.refresh-token')).toBeNull()
  })

  it('reports readiness from the health endpoint and treats network errors as not ready', async () => {
    mockApi({ 'GET /health/ready': new Response('Healthy', { status: 200 }) })
    let client = await loadClient()
    await expect(client.checkReadiness()).resolves.toBe(true)

    mockApi({ 'GET /health/ready': new Response('Unhealthy', { status: 503 }) })
    client = await loadClient()
    await expect(client.checkReadiness()).resolves.toBe(false)

    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')))
    client = await loadClient()
    await expect(client.checkReadiness()).resolves.toBe(false)
  })

  it('wraps non-JSON error bodies instead of crashing', async () => {
    mockApi({ 'GET /api/v1/products/9': new Response('gateway timeout', { status: 504 }) })
    const client = await loadClient()

    await expect(client.getProduct(9)).rejects.toMatchObject({ status: 504, message: 'gateway timeout' })
  })
})
