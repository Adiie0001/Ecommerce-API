import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { admin, customer, jsonResponse, mockApi, page, problem, product, session, stats } from './test/mockApi'

async function renderApp() {
  vi.resetModules()
  const { default: App } = await import('./App.jsx')
  return render(<App />)
}

const dashboardRoutes = {
  'GET /api/v1/products': page([product({ name: 'Wireless Mouse' }), product({ id: 2, name: 'Desk Lamp', stock: 0 })]),
  'GET /api/v1/products/stats': stats,
  'GET /health/ready': new Response('Healthy', { status: 200 }),
}

async function signIn(user, email = 'admin@shop.test') {
  await userEvent.type(await screen.findByLabelText('Email'), email)
  await userEvent.type(screen.getByLabelText('Password'), 'Correct-Horse-9')
  await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))
  return user
}

describe('App', () => {
  beforeEach(() => {
    sessionStorage.clear()
  })

  it('asks anonymous visitors to sign in and does not call the catalog API', async () => {
    const api = mockApi({})
    await renderApp()

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeInTheDocument()
    expect(api.calls).toHaveLength(0)
  })

  it('shows real data after an administrator signs in', async () => {
    mockApi({ 'POST /api/v1/auth/login': session(), ...dashboardRoutes })
    await renderApp()

    await signIn(admin)

    expect(await screen.findByText('Shop Admin')).toBeInTheDocument()
    expect(await screen.findByText('Wireless Mouse')).toBeInTheDocument()
    expect(screen.getByText('Desk Lamp')).toBeInTheDocument()
    expect(await screen.findByText('2,356.90')).toBeInTheDocument()
    expect(await screen.findByText('API ready')).toBeInTheDocument()
  })

  it('shows the server message when the password is wrong', async () => {
    mockApi({
      'POST /api/v1/auth/login': jsonResponse(401, problem(401, { detail: 'Invalid email or password.' })),
    })
    await renderApp()

    await signIn(null)

    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid email or password.')
  })

  it('turns customers away and ends the session it just opened', async () => {
    const api = mockApi({
      'POST /api/v1/auth/login': session(customer),
      'POST /api/v1/auth/logout': new Response(null, { status: 204 }),
    })
    await renderApp()

    await signIn(customer, 'buyer@shop.test')

    expect(await screen.findByRole('alert')).toHaveTextContent('administrators')
    expect(api.callsTo('POST /api/v1/auth/logout')).toHaveLength(1)
    expect(sessionStorage.getItem('ecommerce-admin.refresh-token')).toBeNull()
  })

  it('resumes a stored session without asking for credentials again', async () => {
    sessionStorage.setItem('ecommerce-admin.refresh-token', 'refresh-1')
    mockApi({
      'POST /api/v1/auth/refresh': session(admin, 2),
      'GET /api/v1/auth/me': admin,
      ...dashboardRoutes,
    })
    await renderApp()

    expect(await screen.findByText('Shop Admin')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Sign in' })).not.toBeInTheDocument()
  })

  it('does not resume a session that belongs to a customer', async () => {
    sessionStorage.setItem('ecommerce-admin.refresh-token', 'refresh-1')
    mockApi({
      'POST /api/v1/auth/refresh': session(customer, 2),
      'GET /api/v1/auth/me': customer,
    })
    await renderApp()

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeInTheDocument()
  })

  it('returns to the sign-in form after signing out', async () => {
    const api = mockApi({
      'POST /api/v1/auth/login': session(),
      'POST /api/v1/auth/logout': new Response(null, { status: 204 }),
      ...dashboardRoutes,
    })
    await renderApp()
    await signIn(admin)
    await screen.findByText('Shop Admin')

    await userEvent.click(screen.getByRole('button', { name: 'Sign out' }))

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeInTheDocument()
    expect(api.callsTo('POST /api/v1/auth/logout')[0].body).toEqual({ refreshToken: 'refresh-1' })
  })

  it('falls back to the sign-in form when the session expires mid-use', async () => {
    mockApi({
      'POST /api/v1/auth/login': session(),
      'POST /api/v1/auth/refresh': jsonResponse(401, problem(401)),
      'GET /api/v1/products': jsonResponse(401, problem(401)),
      'GET /api/v1/products/stats': jsonResponse(401, problem(401)),
      'GET /health/ready': new Response('Healthy', { status: 200 }),
    })
    await renderApp()

    await signIn(admin)

    await waitFor(() => expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument())
  })
})
