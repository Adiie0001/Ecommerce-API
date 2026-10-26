import { useState } from 'react'
import { ApiError, createProduct, getProduct, updateProduct } from '../api/client'

const inputClass =
  'mt-1 block w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500'

function toFormValues(product) {
  return {
    name: product?.name ?? '',
    description: product?.description ?? '',
    price: product ? String(product.price) : '',
    stock: product ? String(product.stock) : '',
  }
}

/** Create or edit dialog. Editing sends the version it loaded, so a concurrent change surfaces as a conflict. */
export default function ProductForm({ product, onClose, onSaved }) {
  const editing = Boolean(product)
  const [current, setCurrent] = useState(product)
  const [values, setValues] = useState(toFormValues(product))
  const [fieldErrors, setFieldErrors] = useState({})
  const [message, setMessage] = useState('')
  const [conflict, setConflict] = useState(false)
  const [saving, setSaving] = useState(false)

  const set = (field) => (event) => setValues((previous) => ({ ...previous, [field]: event.target.value }))

  async function handleSubmit(event) {
    event.preventDefault()
    setSaving(true)
    setFieldErrors({})
    setMessage('')
    setConflict(false)

    const payload = {
      name: values.name,
      description: values.description,
      price: Number(values.price),
      stock: Number(values.stock),
    }

    try {
      if (editing) {
        await updateProduct(current.id, { ...payload, version: current.version })
      } else {
        await createProduct(payload)
      }
      onSaved()
    } catch (err) {
      if (!(err instanceof ApiError)) {
        setMessage('The API could not be reached. Try again.')
      } else if (err.status === 409) {
        setConflict(true)
        setMessage(err.message)
      } else if (err.status === 400) {
        setFieldErrors(err.fieldErrors)
        setMessage('Please fix the highlighted fields.')
      } else {
        setMessage(err.message)
      }
    } finally {
      setSaving(false)
    }
  }

  async function reloadLatest() {
    try {
      const latest = await getProduct(current.id)
      setCurrent(latest)
      setValues(toFormValues(latest))
      setConflict(false)
      setMessage('Loaded the latest values. Re-apply your change and save again.')
    } catch (err) {
      setMessage(err.message)
    }
  }

  const fieldError = (field) => fieldErrors[field]?.[0]

  return (
    <div className="fixed inset-0 z-20 flex items-center justify-center bg-black/40 px-4">
      <form
        role="dialog"
        aria-modal="true"
        aria-labelledby="product-form-title"
        onSubmit={handleSubmit}
        className="w-full max-w-md bg-white rounded-xl shadow-lg p-6 space-y-4"
      >
        <h2 id="product-form-title" className="text-lg font-semibold text-gray-900">
          {editing ? 'Edit product' : 'Add product'}
        </h2>

        <label className="block text-sm font-medium text-gray-700">
          Name
          <input className={inputClass} value={values.name} onChange={set('name')} required maxLength={100} />
          {fieldError('name') && <span className="text-xs text-red-700">{fieldError('name')}</span>}
        </label>

        <label className="block text-sm font-medium text-gray-700">
          Description
          <textarea className={inputClass} rows={3} value={values.description} onChange={set('description')} required maxLength={500} />
          {fieldError('description') && <span className="text-xs text-red-700">{fieldError('description')}</span>}
        </label>

        <div className="grid grid-cols-2 gap-4">
          <label className="block text-sm font-medium text-gray-700">
            Price
            <input className={inputClass} type="number" step="0.01" min="0.01" value={values.price} onChange={set('price')} required />
            {fieldError('price') && <span className="text-xs text-red-700">{fieldError('price')}</span>}
          </label>
          <label className="block text-sm font-medium text-gray-700">
            Stock
            <input className={inputClass} type="number" step="1" min="0" value={values.stock} onChange={set('stock')} required />
            {fieldError('stock') && <span className="text-xs text-red-700">{fieldError('stock')}</span>}
          </label>
        </div>

        {message && (
          <p role="alert" className={`text-sm rounded-lg px-3 py-2 border ${conflict ? 'text-amber-800 bg-amber-50 border-amber-200' : 'text-gray-800 bg-gray-50 border-gray-200'}`}>
            {message}
          </p>
        )}

        <div className="flex justify-end gap-3">
          {conflict && (
            <button type="button" onClick={reloadLatest} className="rounded-lg border border-amber-300 px-4 py-2 text-sm text-amber-900 hover:bg-amber-50">
              Load latest values
            </button>
          )}
          <button type="button" onClick={onClose} className="rounded-lg border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50">
            Cancel
          </button>
          <button type="submit" disabled={saving} className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-60">
            {saving ? 'Saving…' : 'Save'}
          </button>
        </div>
      </form>
    </div>
  )
}
