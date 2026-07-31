import { useCallback, useEffect, useState } from 'react'
import { api } from './api'
import { BukrsPicker } from './BukrsPicker'
import type { OrgItem, UserListItem } from './types'

type Dialog =
  | { kind: 'create' }
  | { kind: 'edit'; user: UserListItem }
  | { kind: 'bukrs'; user: UserListItem }
  | { kind: 'password'; user: UserListItem }
  | null

export function UserAdmin() {
  const [users, setUsers] = useState<UserListItem[]>([])
  const [orgs, setOrgs] = useState<OrgItem[]>([])
  const [q, setQ] = useState('')
  const [includeInactive, setIncludeInactive] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [dialog, setDialog] = useState<Dialog>(null)

  const reload = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setUsers(await api.listUsers(q, includeInactive))
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setLoading(false)
    }
  }, [q, includeInactive])

  useEffect(() => { void reload() }, [reload])

  // Danh muc don vi tai 1 lan, dung chung cho moi dialog.
  useEffect(() => {
    api.listOrgs()
      .then(setOrgs)
      .catch(err => setError(err instanceof Error ? err.message : String(err)))
  }, [])

  async function afterSave() {
    setDialog(null)
    await reload()
  }

  return (
    <>
      <div className="toolbar">
        <input className="search" placeholder="Tìm theo tên đăng nhập / họ tên…"
               value={q} onChange={e => setQ(e.target.value)} />
        <label className="inline">
          <input type="checkbox" checked={includeInactive}
                 onChange={e => setIncludeInactive(e.target.checked)} />
          Hiện cả tài khoản đã tắt
        </label>
        <span className="spacer" />
        <button className="primary" onClick={() => setDialog({ kind: 'create' })}>
          + Tạo người dùng
        </button>
      </div>

      {error && <div className="alert error">{error}</div>}

      <div className="card">
        <table>
          <thead>
            <tr>
              <th>Tên đăng nhập</th>
              <th>Họ tên</th>
              <th>Email</th>
              <th>Vai trò</th>
              <th>Đơn vị (BUKRS)</th>
              <th>Trạng thái</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {users.map(u => (
              <tr key={u.id} className={u.isActive ? '' : 'off'}>
                <td><b>{u.username}</b></td>
                <td>{u.fullName ?? <span className="muted">—</span>}</td>
                <td>{u.email ?? <span className="muted">—</span>}</td>
                <td>
                  {u.roles.length === 0
                    ? <span className="muted">chưa gán</span>
                    : u.roles.map(r => <span key={r} className="tag">{r}</span>)}
                </td>
                <td>
                  {u.bukrs.length === 0
                    ? <span className="muted">chưa gán</span>
                    : u.bukrs.map((b, i) => (
                        <span key={b} className={'tag' + (i === 0 ? ' pri' : '')}
                              title={i === 0 ? 'Đơn vị chính' : undefined}>{b}</span>
                      ))}
                </td>
                <td>
                  {u.isActive
                    ? <span className="badge on">Đang bật</span>
                    : <span className="badge">Đã tắt</span>}
                </td>
                <td className="actions">
                  <button onClick={() => setDialog({ kind: 'bukrs', user: u })}>Đơn vị</button>
                  <button onClick={() => setDialog({ kind: 'edit', user: u })}>Sửa</button>
                  <button onClick={() => setDialog({ kind: 'password', user: u })}>Mật khẩu</button>
                </td>
              </tr>
            ))}
            {!loading && users.length === 0 && (
              <tr><td colSpan={7} className="muted pad">Không có người dùng nào khớp.</td></tr>
            )}
            {loading && (
              <tr><td colSpan={7} className="muted pad">Đang tải…</td></tr>
            )}
          </tbody>
        </table>
      </div>

      {dialog?.kind === 'create' &&
        <CreateDialog orgs={orgs} onClose={() => setDialog(null)} onSaved={afterSave} />}
      {dialog?.kind === 'edit' &&
        <EditDialog user={dialog.user} onClose={() => setDialog(null)} onSaved={afterSave} />}
      {dialog?.kind === 'bukrs' &&
        <BukrsDialog user={dialog.user} orgs={orgs} onClose={() => setDialog(null)} onSaved={afterSave} />}
      {dialog?.kind === 'password' &&
        <PasswordDialog user={dialog.user} onClose={() => setDialog(null)} onSaved={afterSave} />}
    </>
  )
}

// ---------------------------------------------------------------- dialog dung chung

function Modal(
  { title, onClose, children }: { title: string; onClose: () => void; children: React.ReactNode },
) {
  return (
    <div className="overlay" onClick={onClose}>
      <div className="card modal" onClick={e => e.stopPropagation()}>
        <div className="modal-head">
          <h2>{title}</h2>
          <button className="icon" onClick={onClose} aria-label="Đóng">✕</button>
        </div>
        {children}
      </div>
    </div>
  )
}

/** Gom trang thai busy + hien loi cho moi form, tranh lap o 4 dialog. */
function useSubmit(onSaved: () => void | Promise<void>) {
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const run = async (action: () => Promise<unknown>) => {
    setError(null)
    setBusy(true)
    try {
      await action()
      await onSaved()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  return { error, busy, run }
}

// ---------------------------------------------------------------- tao nguoi dung

function CreateDialog(
  { orgs, onClose, onSaved }: { orgs: OrgItem[]; onClose: () => void; onSaved: () => Promise<void> },
) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [fullName, setFullName] = useState('')
  const [email, setEmail] = useState('')
  const [isActive, setIsActive] = useState(true)
  const [bukrs, setBukrs] = useState<string[]>([])
  const [primary, setPrimary] = useState<string | null>(null)
  const { error, busy, run } = useSubmit(onSaved)

  return (
    <Modal title="Tạo người dùng" onClose={onClose}>
      <form onSubmit={e => {
        e.preventDefault()
        void run(() => api.createUser({
          username, password, fullName, email, isActive,
          bukrs, primaryBukrs: primary,
        }))
      }}>
        <div className="grid2">
          <label>Tên đăng nhập *
            <input value={username} onChange={e => setUsername(e.target.value)} required autoFocus />
          </label>
          <label>Mật khẩu * <span className="muted">(≥ 8 ký tự)</span>
            <input type="password" value={password} onChange={e => setPassword(e.target.value)}
                   minLength={8} required />
          </label>
          <label>Họ tên
            <input value={fullName} onChange={e => setFullName(e.target.value)} />
          </label>
          <label>Email
            <input type="email" value={email} onChange={e => setEmail(e.target.value)} />
          </label>
        </div>

        <label className="inline">
          <input type="checkbox" checked={isActive} onChange={e => setIsActive(e.target.checked)} />
          Kích hoạt tài khoản
        </label>

        <h3>Đơn vị được phép (BUKRS)</h3>
        <p className="muted">
          Đây là phạm vi tài khoản được gọi <code>/api/bieumau/*</code>. Không gán đơn vị nào thì
          mọi lời gọi biểu mẫu sẽ bị chặn 403.
        </p>
        <BukrsPicker orgs={orgs} selected={bukrs} primary={primary}
                     onChange={(s, p) => { setBukrs(s); setPrimary(p) }} />

        {error && <div className="alert error">{error}</div>}

        <div className="modal-foot">
          <button type="button" onClick={onClose}>Huỷ</button>
          <button className="primary" type="submit" disabled={busy}>
            {busy ? 'Đang lưu…' : 'Tạo'}
          </button>
        </div>
      </form>
    </Modal>
  )
}

// ---------------------------------------------------------------- sua thong tin

function EditDialog(
  { user, onClose, onSaved }:
  { user: UserListItem; onClose: () => void; onSaved: () => Promise<void> },
) {
  const [fullName, setFullName] = useState(user.fullName ?? '')
  const [email, setEmail] = useState(user.email ?? '')
  const [isActive, setIsActive] = useState(user.isActive)
  const { error, busy, run } = useSubmit(onSaved)

  return (
    <Modal title={`Sửa: ${user.username}`} onClose={onClose}>
      <form onSubmit={e => {
        e.preventDefault()
        void run(() => api.updateUser(user.id, { fullName, email, isActive }))
      }}>
        <div className="grid2">
          <label>Họ tên
            <input value={fullName} onChange={e => setFullName(e.target.value)} autoFocus />
          </label>
          <label>Email
            <input type="email" value={email} onChange={e => setEmail(e.target.value)} />
          </label>
        </div>

        <label className="inline">
          <input type="checkbox" checked={isActive} onChange={e => setIsActive(e.target.checked)} />
          Kích hoạt tài khoản <span className="muted">(bỏ tick = tắt, không xoá dữ liệu)</span>
        </label>

        {error && <div className="alert error">{error}</div>}

        <div className="modal-foot">
          <button type="button" onClick={onClose}>Huỷ</button>
          <button className="primary" type="submit" disabled={busy}>
            {busy ? 'Đang lưu…' : 'Lưu'}
          </button>
        </div>
      </form>
    </Modal>
  )
}

// ---------------------------------------------------------------- gan don vi

function BukrsDialog(
  { user, orgs, onClose, onSaved }:
  { user: UserListItem; orgs: OrgItem[]; onClose: () => void; onSaved: () => Promise<void> },
) {
  // Backend tra danh sach voi don vi chinh o dau.
  const [bukrs, setBukrs] = useState<string[]>(user.bukrs)
  const [primary, setPrimary] = useState<string | null>(user.bukrs[0] ?? null)
  const { error, busy, run } = useSubmit(onSaved)

  return (
    <Modal title={`Đơn vị của: ${user.username}`} onClose={onClose}>
      <form onSubmit={e => {
        e.preventDefault()
        void run(() => api.assignBukrs(user.id, { bukrs, primaryBukrs: primary }))
      }}>
        <p className="muted">
          Lưu là <b>thay toàn bộ</b> danh sách cũ. Gán đơn vị cha thì tài khoản được cả các đơn vị
          trực thuộc bên dưới.
        </p>

        <BukrsPicker orgs={orgs} selected={bukrs} primary={primary}
                     onChange={(s, p) => { setBukrs(s); setPrimary(p) }} />

        {error && <div className="alert error">{error}</div>}

        <div className="modal-foot">
          <button type="button" onClick={onClose}>Huỷ</button>
          <button className="primary" type="submit" disabled={busy}>
            {busy ? 'Đang lưu…' : 'Lưu'}
          </button>
        </div>
      </form>
    </Modal>
  )
}

// ---------------------------------------------------------------- doi mat khau

function PasswordDialog(
  { user, onClose, onSaved }:
  { user: UserListItem; onClose: () => void; onSaved: () => Promise<void> },
) {
  const [pwd, setPwd] = useState('')
  const [confirm, setConfirm] = useState('')
  const { error, busy, run } = useSubmit(onSaved)
  const mismatch = confirm.length > 0 && pwd !== confirm

  return (
    <Modal title={`Đặt lại mật khẩu: ${user.username}`} onClose={onClose}>
      <form onSubmit={e => {
        e.preventDefault()
        void run(() => api.changePassword(user.id, pwd))
      }}>
        <label>Mật khẩu mới * <span className="muted">(≥ 8 ký tự)</span>
          <input type="password" value={pwd} onChange={e => setPwd(e.target.value)}
                 minLength={8} required autoFocus />
        </label>
        <label>Nhập lại *
          <input type="password" value={confirm} onChange={e => setConfirm(e.target.value)}
                 minLength={8} required />
        </label>

        {mismatch && <div className="alert error">Hai lần nhập không khớp.</div>}
        {error && <div className="alert error">{error}</div>}

        <div className="modal-foot">
          <button type="button" onClick={onClose}>Huỷ</button>
          <button className="primary" type="submit" disabled={busy || mismatch}>
            {busy ? 'Đang lưu…' : 'Đổi mật khẩu'}
          </button>
        </div>
      </form>
    </Modal>
  )
}
