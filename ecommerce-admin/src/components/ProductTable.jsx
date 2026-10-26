import { useEffect, useState } from 'react'
import { deleteProduct, listProducts } from '../api/client'
import { useDebouncedValue } from '../hooks/useDebouncedValue'
import ProductForm from './ProductForm'

const PAGE_SIZE = 10
const money = new Intl.NumberFormat('en', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

function StockBadge({ stock }) {
  if (stock === 0) return <span className="px-2 py-0.5 text-xs font-semibold rounded-full bg-red-100 text-red-800">Out of stock</span>
  if (stock <= 10) return <span className="px-2 py-0.5 text-xs font-semibold rounded-full bg-yellow-100 text-yellow-800">Low</span>
  return <span className="px-2 py-0.5 text-xs font-semibold rounded-full bg-green-100 text-green-800">In stock</span>
}

export default function ProductTable({ onChanged }) {
  const [query, setQuery] = useState('')
  const [sort, setSort] = useState('name')
  const [order, setOrder] = useState('asc')
  const [page, setPage] = useState(1)
  const [reloadToken, setReloadToken] = useState(0)
  const [result, setResult] = useState(null)
  const [error, setError] = useState('')
  const [editing, setEditing] = useState(null)
  const [deleting, setDeleting] = useState(null)
  const [notice, setNotice] = useState('')

  const debouncedQuery = useDebouncedValue(query, 300)

  useEffect(() => {
    const controller = new AbortController()
    listProducts({ page, pageSize: PAGE_SIZE, q: debouncedQuery, sort, order }, controller.signal)
      .then((data) => {
        setResult(data)
        setError('')
      })
      .catch((err) => {
        if (err.name !== 'AbortError') setError(err.message)
      })
    return () => controller.abort()
  }, [page, debouncedQuery, sort, order, reloadToken])

  const refresh = () => {
    setReloadToken((token) => token + 1)
    onChanged()
  }

  async function confirmDelete() {
    const target = deleting
    setDeleting(null)
    try {
      await deleteProduct(target.id)
      setNotice(`Deleted “${target.name}”.`)
      refresh()
    } catch (err) {
      setNotice(`Could not delete “${target.name}”: ${err.message}`)
    }
  }

  const items = result?.items ?? []
  const totalPages = Math.max(1, result?.totalPages ?? 1)

  return (
    <section aria-label="Products" className="bg-white rounded-xl border border-gray-200 overflow-hidden">
      <div className="px-4 py-3 border-b border-gray-200 flex flex-wrap items-center gap-3">
        <h2 className="text-lg font-semibold text-gray-900 mr-auto">Products</h2>

        <input
          type="search"
          aria-label="Search products"
          placeholder="Search name or description"
          value={query}
          onChange={(e) => {
            setQuery(e.target.value)
            setPage(1)
          }}
          className="rounded-lg border border-gray-300 px-3 py-2 text-sm w-64"
        />

        <select
          aria-label="Sort by"
          value={sort}
          onChange={(e) => {
            setSort(e.target.value)
            setPage(1)
          }}
          className="rounded-lg border border-gray-300 px-3 py-2 text-sm"
        >
          <option value="name">Name</option>
          <option value="price">Price</option>
          <option value="stock">Stock</option>
          <option value="created">Created</option>
          <option value="updated">Updated</option>
        </select>

        <button
          type="button"
          aria-label={order === 'asc' ? 'Ascending' : 'Descending'}
          onClick={() => setOrder((current) => (current === 'asc' ? 'desc' : 'asc'))}
          className="rounded-lg border border-gray-300 px-3 py-2 text-sm hover:bg-gray-50"
        >
          {order === 'asc' ? '↑ Asc' : '↓ Desc'}
        </button>

        <button type="button" onClick={() => setEditing('new')} className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700">
          Add product
        </button>
      </div>

      {notice && (
        <p role="status" className="px-4 py-2 text-sm bg-indigo-50 text-indigo-900 border-b border-indigo-100">
          {notice}
        </p>
      )}
      {error && (
        <p role="alert" className="px-4 py-2 text-sm bg-red-50 text-red-800 border-b border-red-100">
          Could not load products: {error}
        </p>
      )}

      <div className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="bg-gray-50 text-xs uppercase tracking-wide text-gray-500">
            <tr>
              <th className="px-4 py-3 font-medium">Name</th>
              <th className="px-4 py-3 font-medium text-right">Price</th>
              <th className="px-4 py-3 font-medium text-right">Stock</th>
              <th className="px-4 py-3 font-medium">Status</th>
              <th className="px-4 py-3 font-medium text-right">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {!result && !error && (
              <tr>
                <td colSpan={5} className="px-4 py-8 text-center text-gray-500">Loading…</td>
              </tr>
            )}
            {result && items.length === 0 && (
              <tr>
                <td colSpan={5} className="px-4 py-8 text-center text-gray-500">No products match.</td>
              </tr>
            )}
            {items.map((product) => (
              <tr key={product.id} className="hover:bg-gray-50">
                <td className="px-4 py-3">
                  <div className="font-medium text-gray-900">{product.name}</div>
                  <div className="text-gray-500 truncate max-w-md">{product.description}</div>
                </td>
                <td className="px-4 py-3 text-right tabular-nums">{money.format(product.price)}</td>
                <td className="px-4 py-3 text-right tabular-nums">{product.stock}</td>
                <td className="px-4 py-3"><StockBadge stock={product.stock} /></td>
                <td className="px-4 py-3 text-right whitespace-nowrap">
                  <button type="button" onClick={() => setEditing(product)} aria-label={`Edit ${product.name}`} className="text-indigo-600 hover:text-indigo-900 mr-3">
                    Edit
                  </button>
                  <button type="button" onClick={() => setDeleting(product)} aria-label={`Delete ${product.name}`} className="text-red-600 hover:text-red-900">
                    Delete
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="px-4 py-3 border-t border-gray-200 flex items-center justify-between text-sm text-gray-600">
        <span>{result ? `${result.totalCount} product${result.totalCount === 1 ? '' : 's'}` : ''}</span>
        <div className="flex items-center gap-3">
          <button type="button" disabled={page <= 1} onClick={() => setPage((p) => p - 1)} className="rounded-lg border border-gray-300 px-3 py-1 disabled:opacity-40">
            Previous
          </button>
          <span>Page {page} of {totalPages}</span>
          <button type="button" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)} className="rounded-lg border border-gray-300 px-3 py-1 disabled:opacity-40">
            Next
          </button>
        </div>
      </div>

      {deleting && (
        <div className="fixed inset-0 z-20 flex items-center justify-center bg-black/40 px-4">
          <div role="alertdialog" aria-modal="true" aria-labelledby="delete-title" className="w-full max-w-sm bg-white rounded-xl shadow-lg p-6 space-y-4">
            <h2 id="delete-title" className="text-lg font-semibold text-gray-900">Delete “{deleting.name}”?</h2>
            <p className="text-sm text-gray-600">This removes the product from the catalog and cannot be undone.</p>
            <div className="flex justify-end gap-3">
              <button type="button" onClick={() => setDeleting(null)} className="rounded-lg border border-gray-300 px-4 py-2 text-sm">Cancel</button>
              <button type="button" onClick={confirmDelete} className="rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white hover:bg-red-700">Delete</button>
            </div>
          </div>
        </div>
      )}

      {editing && (
        <ProductForm
          product={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            setNotice('Saved.')
            refresh()
          }}
        />
      )}
    </section>
  )
}
