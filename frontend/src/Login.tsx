import { useState } from 'react'
import { api, setToken } from './api'
import type { UserInfo } from './types'

export function Login({ onDone }: { onDone: (user: UserInfo) => void }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    setBusy(true)
    try {
      const res = await api.login(username, password)
      setToken(res.accessToken)
      onDone(res.user)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login-wrap">
      <form className="card login-card" onSubmit={submit}>
        <h1>ToolExcel</h1>
        <p className="muted">Quản trị người dùng &amp; phân đơn vị</p>

        <label>
          Tên đăng nhập
          <input value={username} onChange={e => setUsername(e.target.value)}
                 autoComplete="username" autoFocus required />
        </label>

        <label>
          Mật khẩu
          <input type="password" value={password} onChange={e => setPassword(e.target.value)}
                 autoComplete="current-password" required />
        </label>

        {error && <div className="alert error">{error}</div>}

        <button className="primary" type="submit" disabled={busy}>
          {busy ? 'Đang đăng nhập…' : 'Đăng nhập'}
        </button>
      </form>
    </div>
  )
}
