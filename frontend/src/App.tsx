import { useEffect, useState } from 'react'
import { api, clearToken, getToken } from './api'
import { ApiTester } from './ApiTester'
import { Login } from './Login'
import { UserAdmin } from './UserAdmin'
import type { UserInfo } from './types'

const ADMIN_ROLES = ['ADMIN', 'SUPER']

type Tab = 'users' | 'api'

export function App() {
  const [user, setUser] = useState<UserInfo | null>(null)
  const [checking, setChecking] = useState(!!getToken())
  const [tab, setTab] = useState<Tab>('users')

  // Con token trong localStorage -> xac nhan lai voi server truoc khi vao man quan tri.
  useEffect(() => {
    if (!getToken()) return
    api.me()
      .then(setUser)
      .catch(() => clearToken())
      .finally(() => setChecking(false))
  }, [])

  function logout() {
    clearToken()
    setUser(null)
  }

  if (checking) return <div className="pad muted">Đang kiểm tra phiên đăng nhập…</div>
  if (!user) return <Login onDone={setUser} />

  const isAdmin = user.roles.some(r => ADMIN_ROLES.includes(r.toUpperCase()))

  return (
    <div className="app">
      <header>
        <div>
          <strong>ToolExcel</strong>
          {isAdmin && (
            <nav className="tabs">
              <button className={tab === 'users' ? 'on' : ''} onClick={() => setTab('users')}>
                Người dùng
              </button>
              <button className={tab === 'api' ? 'on' : ''} onClick={() => setTab('api')}>
                Thử API
              </button>
              <a href="/swagger" target="_blank" rel="noreferrer">Swagger ↗</a>
            </nav>
          )}
        </div>
        <div className="who">
          <span>{user.fullName || user.username}</span>
          {user.roles.map(r => <span key={r} className="tag">{r}</span>)}
          <button onClick={logout}>Đăng xuất</button>
        </div>
      </header>

      <main>
        {!isAdmin ? (
          <div className="alert error">
            Tài khoản <b>{user.username}</b> không có vai trò <code>ADMIN</code> hoặc
            {' '}<code>SUPER</code> nên không vào được màn quản trị.
          </div>
        ) : tab === 'users' ? <UserAdmin /> : <ApiTester me={user} />}
      </main>
    </div>
  )
}
