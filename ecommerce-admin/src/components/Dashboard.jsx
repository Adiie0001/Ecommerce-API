import { useState } from 'react'
import { useAuth } from '../auth/useAuth'
import HealthBadge from './HealthBadge'
import ProductTable from './ProductTable'
import RecommendPanel from './RecommendPanel'
import StatsCards from './StatsCards'

export default function Dashboard() {
  const { user, logout } = useAuth()
  const [statsVersion, setStatsVersion] = useState(0)

  return (
    <div className="min-h-screen">
      <header className="bg-white border-b border-gray-200">
        <div className="max-w-6xl mx-auto px-4 h-16 flex items-center gap-4">
          <h1 className="text-xl font-semibold text-gray-900 mr-auto">Shop admin</h1>
          <HealthBadge />
          <span className="text-sm text-gray-600">{user.displayName}</span>
          <button type="button" onClick={logout} className="rounded-lg border border-gray-300 px-3 py-1.5 text-sm hover:bg-gray-50">
            Sign out
          </button>
        </div>
      </header>

      <main className="max-w-6xl mx-auto px-4 py-6 space-y-6">
        <StatsCards version={statsVersion} />
        <ProductTable onChanged={() => setStatsVersion((v) => v + 1)} />
        <RecommendPanel />
      </main>
    </div>
  )
}
