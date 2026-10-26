import { useState } from 'react'
import { recommend } from '../api/client'

const SOURCES = {
  gemini: { label: 'Ranked by Gemini', style: 'bg-purple-100 text-purple-800' },
  rules: { label: 'Ranked by rules (AI not configured)', style: 'bg-gray-100 text-gray-700' },
  'rules-fallback': { label: 'Ranked by rules (AI unavailable)', style: 'bg-amber-100 text-amber-800' },
}

/** Try the public recommendation endpoint; the badge states which engine produced the ranking. */
export default function RecommendPanel() {
  const [query, setQuery] = useState('')
  const [result, setResult] = useState(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function handleSubmit(event) {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      setResult(await recommend(query.trim()))
    } catch (err) {
      setResult(null)
      setError(err.status === 429 ? 'Too many searches. Wait a moment and try again.' : err.message)
    } finally {
      setBusy(false)
    }
  }

  const source = result ? (SOURCES[result.source] ?? { label: result.source, style: 'bg-gray-100 text-gray-700' }) : null

  return (
    <section aria-label="Recommendation tester" className="bg-white rounded-xl border border-gray-200 p-4 space-y-3">
      <h2 className="text-lg font-semibold text-gray-900">Recommendation tester</h2>
      <form onSubmit={handleSubmit} className="flex gap-3">
        <input
          aria-label="Shopper query"
          placeholder="e.g. cheap wireless mouse"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          minLength={2}
          maxLength={200}
          required
          className="flex-1 rounded-lg border border-gray-300 px-3 py-2 text-sm"
        />
        <button type="submit" disabled={busy} className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-60">
          {busy ? 'Searching…' : 'Recommend'}
        </button>
      </form>

      {error && <p role="alert" className="text-sm text-red-700">{error}</p>}

      {result && (
        <div className="space-y-2">
          <span className={`inline-block text-xs font-semibold px-2 py-1 rounded-full ${source.style}`}>{source.label}</span>
          {result.items.length === 0 ? (
            <p className="text-sm text-gray-600">No relevant products.</p>
          ) : (
            <ol className="space-y-2">
              {result.items.map((item) => (
                <li key={item.product.id} className="flex items-start justify-between gap-4 border border-gray-100 rounded-lg px-3 py-2">
                  <div>
                    <div className="font-medium text-gray-900">{item.product.name}</div>
                    <div className="text-sm text-gray-600">{item.reason}</div>
                  </div>
                  <span className="text-sm font-semibold text-gray-700 tabular-nums">{item.score}</span>
                </li>
              ))}
            </ol>
          )}
        </div>
      )}
    </section>
  )
}
