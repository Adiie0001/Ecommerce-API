import { useEffect, useState } from 'react'
import { getStats } from '../api/client'

const number = new Intl.NumberFormat('en')
const money = new Intl.NumberFormat('en', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

/** Inventory totals computed by the API from the real catalog. Reloads when `version` changes. */
export default function StatsCards({ version }) {
  const [stats, setStats] = useState(null)
  const [error, setError] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    getStats(controller.signal)
      .then((data) => {
        setStats(data)
        setError('')
      })
      .catch((err) => {
        if (err.name !== 'AbortError') setError(err.message)
      })
    return () => controller.abort()
  }, [version])

  if (error) {
    return (
      <p role="alert" className="text-sm text-red-700">
        Could not load inventory totals: {error}
      </p>
    )
  }

  const cards = stats
    ? [
        { label: 'Products', value: number.format(stats.totalProducts) },
        { label: 'Units in stock', value: number.format(stats.totalUnits) },
        { label: `Low stock (≤ ${stats.lowStockThreshold})`, value: number.format(stats.lowStock) },
        { label: 'Out of stock', value: number.format(stats.outOfStock) },
        { label: 'Inventory value', value: money.format(stats.inventoryValue) },
      ]
    : []

  return (
    <section aria-label="Inventory totals" className="grid grid-cols-2 md:grid-cols-5 gap-4">
      {stats
        ? cards.map((card) => (
            <div key={card.label} className="bg-white rounded-xl border border-gray-200 p-4">
              <p className="text-sm text-gray-500">{card.label}</p>
              <p className="text-2xl font-semibold text-gray-900">{card.value}</p>
            </div>
          ))
        : Array.from({ length: 5 }, (_, i) => <div key={i} className="h-20 rounded-xl bg-gray-100 animate-pulse" />)}
    </section>
  )
}
