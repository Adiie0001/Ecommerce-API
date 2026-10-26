import { useEffect, useState } from 'react'
import { checkReadiness } from '../api/client'

/** Shows what /health/ready actually says, refreshed every 30 seconds. No hard-coded status. */
export default function HealthBadge() {
  const [state, setState] = useState('checking')

  useEffect(() => {
    let cancelled = false

    async function poll() {
      const ready = await checkReadiness()
      if (!cancelled) setState(ready ? 'ready' : 'down')
    }

    poll()
    const timer = setInterval(poll, 30_000)
    return () => {
      cancelled = true
      clearInterval(timer)
    }
  }, [])

  const styles = {
    checking: 'bg-gray-100 text-gray-600',
    ready: 'bg-green-100 text-green-800',
    down: 'bg-red-100 text-red-800',
  }
  const labels = { checking: 'Checking API…', ready: 'API ready', down: 'API unavailable' }

  return (
    <span role="status" className={`text-sm font-medium px-3 py-1 rounded-full ${styles[state]}`}>
      {labels[state]}
    </span>
  )
}
