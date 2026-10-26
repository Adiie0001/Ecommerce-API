import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { jsonResponse, mockApi, page, problem, product } from '../test/mockApi'
import ProductTable from './ProductTable'

const mouse = product({ id: 5, name: 'Wireless Mouse', price: 25, stock: 20, version: 'v-mouse' })
const lamp = product({ id: 6, name: 'Desk Lamp', description: 'LED lamp', price: 19.99, stock: 0 })
const kb = product({ id: 7, name: 'Keyboard', price: 80.5, stock: 4 })

describe('ProductTable', () => {
  it('lists products with formatted prices and a stock status', async () => {
    mockApi({ 'GET /api/v1/products': page([mouse, lamp, kb], { totalCount: 3 }) })
    render(<ProductTable onChanged={() => {}} />)

    expect(await screen.findByText('Wireless Mouse')).toBeInTheDocument()
    expect(screen.getByText('25.00')).toBeInTheDocument()
    expect(screen.getByText('19.99')).toBeInTheDocument()
    expect(screen.getByText('80.50')).toBeInTheDocument()
    expect(screen.getByText('In stock')).toBeInTheDocument()
    expect(screen.getByText('Out of stock')).toBeInTheDocument()
    expect(screen.getByText('Low')).toBeInTheDocument()
    expect(screen.getByText('3 products')).toBeInTheDocument()
  })

  it('searches the server after a short pause and returns to page one', async () => {
    const api = mockApi({ 'GET /api/v1/products': page([mouse]) })
    render(<ProductTable onChanged={() => {}} />)
    await screen.findByText('Wireless Mouse')

    await userEvent.type(screen.getByLabelText('Search products'), 'mouse')

    await waitFor(() => expect(api.callsTo('GET /api/v1/products').at(-1).params.get('q')).toBe('mouse'))
    expect(api.callsTo('GET /api/v1/products').at(-1).params.get('page')).toBe('1')
  })

  it('sorts and pages on the server', async () => {
    const api = mockApi({ 'GET /api/v1/products': page([mouse], { totalCount: 25, totalPages: 3 }) })
    render(<ProductTable onChanged={() => {}} />)
    await screen.findByText('Wireless Mouse')
    const last = () => api.callsTo('GET /api/v1/products').at(-1).params

    await userEvent.selectOptions(screen.getByLabelText('Sort by'), 'price')
    await waitFor(() => expect(last().get('sort')).toBe('price'))

    await userEvent.click(screen.getByRole('button', { name: 'Ascending' }))
    await waitFor(() => expect(last().get('order')).toBe('desc'))

    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await waitFor(() => expect(last().get('page')).toBe('2'))
    expect(screen.getByText('Page 2 of 3')).toBeInTheDocument()
  })

  it('disables Previous on the first page and Next on the last', async () => {
    mockApi({ 'GET /api/v1/products': page([mouse], { totalCount: 1, totalPages: 1 }) })
    render(<ProductTable onChanged={() => {}} />)
    await screen.findByText('Wireless Mouse')

    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()
  })

  it('shows an empty state and a load error', async () => {
    mockApi({ 'GET /api/v1/products': page([], { totalCount: 0 }) })
    const { unmount } = render(<ProductTable onChanged={() => {}} />)
    expect(await screen.findByText('No products match.')).toBeInTheDocument()
    unmount()

    mockApi({ 'GET /api/v1/products': jsonResponse(500, problem(500, { detail: 'An unexpected error occurred.' })) })
    render(<ProductTable onChanged={() => {}} />)
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not load products: An unexpected error occurred.')
  })

  it('creates a product with numeric price and stock, then reloads the list', async () => {
    const onChanged = vi.fn()
    const api = mockApi({
      'GET /api/v1/products': page([mouse]),
      'POST /api/v1/products': jsonResponse(201, product({ id: 9 })),
    })
    render(<ProductTable onChanged={onChanged} />)
    await screen.findByText('Wireless Mouse')

    await userEvent.click(screen.getByRole('button', { name: 'Add product' }))
    const dialog = screen.getByRole('dialog')
    await userEvent.type(within(dialog).getByLabelText('Name'), 'Webcam')
    await userEvent.type(within(dialog).getByLabelText('Description'), 'HD camera')
    await userEvent.type(within(dialog).getByLabelText('Price'), '45.5')
    await userEvent.type(within(dialog).getByLabelText('Stock'), '8')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(api.callsTo('POST /api/v1/products')[0].body).toEqual({ name: 'Webcam', description: 'HD camera', price: 45.5, stock: 8 })
    expect(onChanged).toHaveBeenCalledTimes(1)
    expect(api.callsTo('GET /api/v1/products')).toHaveLength(2)
    expect(screen.getByRole('status')).toHaveTextContent('Saved.')
  })

  it('shows the server validation message next to the field', async () => {
    mockApi({
      'GET /api/v1/products': page([mouse]),
      'POST /api/v1/products': jsonResponse(400, problem(400, { errors: { price: ['Price can have at most two decimal places.'] } })),
    })
    render(<ProductTable onChanged={() => {}} />)
    await screen.findByText('Wireless Mouse')

    await userEvent.click(screen.getByRole('button', { name: 'Add product' }))
    const dialog = screen.getByRole('dialog')
    await userEvent.type(within(dialog).getByLabelText('Name'), 'X')
    await userEvent.type(within(dialog).getByLabelText('Description'), 'Y')
    await userEvent.type(within(dialog).getByLabelText('Price'), '5')
    await userEvent.type(within(dialog).getByLabelText('Stock'), '1')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await within(dialog).findByText('Price can have at most two decimal places.')).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('sends the loaded version when editing and recovers from a conflict by loading the latest values', async () => {
    let attempts = 0
    const api = mockApi({
      'GET /api/v1/products': page([mouse]),
      'GET /api/v1/products/5': product({ id: 5, name: 'Wireless Mouse v2', price: 27, stock: 20, version: 'v-new' }),
      'PUT /api/v1/products/5': () =>
        ++attempts === 1
          ? jsonResponse(409, problem(409, { detail: 'The product was changed by someone else. Reload it and apply your edit again.' }))
          : jsonResponse(200, product({ id: 5 })),
    })
    render(<ProductTable onChanged={() => {}} />)
    await screen.findByText('Wireless Mouse')

    await userEvent.click(screen.getByRole('button', { name: 'Edit Wireless Mouse' }))
    const dialog = screen.getByRole('dialog')
    await userEvent.clear(within(dialog).getByLabelText('Stock'))
    await userEvent.type(within(dialog).getByLabelText('Stock'), '15')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await within(dialog).findByText(/changed by someone else/)).toBeInTheDocument()
    expect(api.callsTo('PUT /api/v1/products/5')[0].body.version).toBe('v-mouse')

    await userEvent.click(within(dialog).getByRole('button', { name: 'Load latest values' }))
    await waitFor(() => expect(within(dialog).getByLabelText('Name')).toHaveValue('Wireless Mouse v2'))

    await userEvent.click(within(dialog).getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(api.callsTo('PUT /api/v1/products/5')[1].body.version).toBe('v-new')
  })

  it('asks for confirmation before deleting and removes the product', async () => {
    const api = mockApi({
      'GET /api/v1/products': page([mouse]),
      'DELETE /api/v1/products/5': new Response(null, { status: 204 }),
    })
    render(<ProductTable onChanged={() => {}} />)
    await screen.findByText('Wireless Mouse')

    await userEvent.click(screen.getByRole('button', { name: 'Delete Wireless Mouse' }))
    expect(api.callsTo('DELETE /api/v1/products/5')).toHaveLength(0)

    const confirm = screen.getByRole('alertdialog')
    await userEvent.click(within(confirm).getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(api.callsTo('DELETE /api/v1/products/5')).toHaveLength(1))
    expect(await screen.findByRole('status')).toHaveTextContent('Deleted “Wireless Mouse”.')
  })

  it('does nothing when deletion is cancelled', async () => {
    const api = mockApi({ 'GET /api/v1/products': page([mouse]) })
    render(<ProductTable onChanged={() => {}} />)
    await screen.findByText('Wireless Mouse')

    await userEvent.click(screen.getByRole('button', { name: 'Delete Wireless Mouse' }))
    await userEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Cancel' }))

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    expect(api.calls.filter((c) => c.method === 'DELETE')).toHaveLength(0)
  })

  it('reports a failed deletion instead of pretending it worked', async () => {
    mockApi({
      'GET /api/v1/products': page([mouse]),
      'DELETE /api/v1/products/5': jsonResponse(404, problem(404, { detail: 'Product 5 was not found.' })),
    })
    render(<ProductTable onChanged={() => {}} />)
    await screen.findByText('Wireless Mouse')

    await userEvent.click(screen.getByRole('button', { name: 'Delete Wireless Mouse' }))
    await userEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Delete' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Could not delete “Wireless Mouse”: Product 5 was not found.')
  })
})
