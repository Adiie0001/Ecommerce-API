import { AuthProvider } from './auth/AuthProvider'
import { useAuth } from './auth/useAuth'
import Dashboard from './components/Dashboard'
import LoginForm from './components/LoginForm'

function Gate() {
  const { status } = useAuth()

  if (status === 'checking') {
    return <p className="min-h-screen flex items-center justify-center text-gray-500">Loading…</p>
  }

  return status === 'authenticated' ? <Dashboard /> : <LoginForm />
}

export default function App() {
  return (
    <AuthProvider>
      <Gate />
    </AuthProvider>
  )
}
