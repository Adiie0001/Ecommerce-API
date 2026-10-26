import { vi } from 'vitest'

export const admin = { id: 1, email: 'admin@shop.test', displayName: 'Shop Admin', role: 'Admin' }
export const customer = { id: 2, email: 'buyer@shop.test', displayName: 'Buyer', role: 'Customer' }

export const session = (user = admin, n = 1) => ({
  accessToken: `access-${n}`,
  tokenType: 'Bearer',
  expiresIn: 900,
  refreshToken: `refresh-${n}`,
  user,
})

export function product(overrides = {}) {
  return {
    id: 1,
    name: 'Wireless Mouse',
    description: 'Ergonomic wireless mouse',
    price: 25,
    stock: 20,
    version: 'v1',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

export function page(items, overrides = {}) {
  return { items, page: 1, pageSize: 10, totalCount: items.length, totalPages: 1, ...overrides }
}

export const stats = {
  totalProducts: 12,
  outOfStock: 2,
  lowStock: 4,
  lowStockThreshold: 10,
  totalUnits: 1500,
  inventoryValue: 2356.9,
}

export function problem(status, fields = {}) {
  return { status, title: 'Problem', detail: fields.detail, errors: fields.errors, correlationId: 'corr-1' }
}

export function jsonResponse(status, body) {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

/**
 * Replaces fetch with a tiny router. Route keys look like "GET /api/v1/products". A route is a value (200 JSON),
 * a Response, or a function receiving { key, url, method, headers, body, params } and returning either.
 */
export function mockApi(routes) {
  const calls = []

  const fetchMock = vi.fn(async (input, init = {}) => {
    const url = new URL(typeof input === 'string' ? input : input.url)
    const method = (init.method ?? 'GET').toUpperCase()
    const key = `${method} ${url.pathname}`
    const call = {
      key,
      url,
      method,
      headers: init.headers ?? {},
      body: init.body ? JSON.parse(init.body) : undefined,
      params: url.searchParams,
    }
    calls.push(call)

    const route = routes[key]
    if (route === undefined) throw new Error(`Unexpected request: ${key}`)

    const result = typeof route === 'function' ? await route(call) : route
    return result instanceof Response ? result : jsonResponse(200, result)
  })

  vi.stubGlobal('fetch', fetchMock)
  return { calls, callsTo: (key) => calls.filter((c) => c.key === key) }
}
