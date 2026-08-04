import { useEffect, useMemo, useState } from 'react'
import { api, probe } from './api'
import type { BieuMauItem, OrgItem, ProbeResult, UserInfo } from './types'

/** Tham so header cua bieu mau — dung ten cot, gui len duoi dang h_<COL>. */
const HEADER_COLS = ['BUKRS', 'YEAR', 'PERIOD', 'DAY', 'WERKS'] as const

/**
 * Bat trinh duyet tai file ngay. Dung cho nut "Tai template" vi muc dich cua no la
 * LAY FILE — bat nguoi dung bam them "Tai xuong" la mot buoc vo ich. Khoi "Goi endpoint
 * bat ky" thi KHONG tu tai: o do dang do endpoint, khong nen do rac vao thu muc Downloads.
 */
function triggerDownload(file: { name: string; url: string }) {
  const a = document.createElement('a')
  a.href = file.url
  a.download = file.name
  document.body.appendChild(a)
  a.click()
  a.remove()
}

export function ApiTester({ me }: { me: UserInfo }) {
  // Token dung cho CAC LAN GOI THU. null = dung token cua chinh minh.
  const [asToken, setAsToken] = useState<string | null>(null)
  const [asUser, setAsUser] = useState<string | null>(null)
  const [asScope, setAsScope] = useState<string[] | null | undefined>(undefined)

  const [result, setResult] = useState<ProbeResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Danh muc don vi de h_BUKRS thanh dropdown. Loi o day KHONG lam vo tab: rot ve o go tay.
  const [orgs, setOrgs] = useState<OrgItem[]>([])
  useEffect(() => { api.listOrgs().then(setOrgs).catch(() => setOrgs([])) }, [])

  async function run(fn: () => Promise<ProbeResult>, autoDownload = false) {
    setBusy(true)
    setError(null)
    try {
      const r = await fn()
      setResult(r)
      if (autoDownload && r.file) triggerDownload(r.file)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setResult(null)
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <p className="muted">
        Gọi thẳng các endpoint bằng token thật, xem nguyên mã trạng thái và nội dung trả về.
        Lời gọi ở đây <b>không</b> làm bạn bị đăng xuất khi gặp 401.
      </p>

      <TokenBox
        me={me} asUser={asUser} asScope={asScope}
        onUse={(token, username, scope) => { setAsToken(token); setAsUser(username); setAsScope(scope) }}
        onReset={() => { setAsToken(null); setAsUser(null); setAsScope(undefined) }}
        onError={setError}
      />

      <BieuMauBox
        token={asToken} busy={busy} onRun={run} orgs={orgs}
        myScope={asUser === null ? me.allowedBukrs : (asScope ?? null)}
        lastFile={result?.file ?? null}
      />
      <FreeCallBox token={asToken} busy={busy} onRun={run} />

      {error && <div className="alert error">{error}</div>}
      {busy && <p className="muted pad">Đang gọi…</p>}
      {result && <ResultBox r={result} />}
    </>
  )
}

// ---------------------------------------------------------------- doi token

function TokenBox(
  { me, asUser, asScope, onUse, onReset, onError }: {
    me: UserInfo
    asUser: string | null
    asScope: string[] | null | undefined
    onUse: (token: string, username: string, scope: string[] | null) => void
    onReset: () => void
    onError: (m: string) => void
  },
) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)

  async function getToken(e: React.FormEvent) {
    e.preventDefault()
    setBusy(true)
    try {
      // Dung POST /api/auth/token — dung endpoint ma APEX se goi.
      const r = await probe('POST', '/api/auth/token', {
        token: null,
        jsonBody: JSON.stringify({ username: username.trim(), password }),
      })
      if (r.status !== 200 || !r.body) {
        onError(`Lấy token thất bại (HTTP ${r.status}): ${r.body ?? ''}`)
        return
      }
      const data = JSON.parse(r.body)
      onUse(data.accessToken, username.trim(), data.allowedBukrs ?? null)
      setPassword('')
    } catch (err) {
      onError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="card pad tester-box">
      <h3>Gọi bằng tài khoản nào</h3>

      {asUser === null ? (
        <p className="muted">
          Đang dùng token của <b>{me.username}</b> · vai trò {me.roles.join(', ') || '(chưa gán)'} ·
          {' '}phạm vi {me.allowedBukrs === null
            ? <b>không giới hạn (SUPER)</b>
            : me.allowedBukrs.length === 0
              ? <b>chưa gán đơn vị nào</b>
              : <b>{me.allowedBukrs.join(', ')}</b>}
        </p>
      ) : (
        <div className="alert warn">
          Đang gọi thử bằng token của <b>{asUser}</b> · phạm vi {asScope === null
            ? <b>không giới hạn (SUPER)</b>
            : (asScope?.length ?? 0) === 0
              ? <b>chưa gán đơn vị nào — mọi lời gọi biểu mẫu sẽ bị 403</b>
              : <b>{asScope!.join(', ')}</b>}
          {' '}
          <button type="button" onClick={onReset}>Về token của tôi</button>
        </div>
      )}

      <form onSubmit={getToken} className="row">
        <label>Tên đăng nhập
          <input value={username} onChange={e => setUsername(e.target.value)}
                 placeholder="vd apiexport" required />
        </label>
        <label>Mật khẩu
          <input type="password" value={password} onChange={e => setPassword(e.target.value)}
                 autoComplete="off" required />
        </label>
        <button type="submit" disabled={busy}>
          {busy ? 'Đang lấy…' : 'Lấy token & dùng để thử'}
        </button>
      </form>
      <p className="muted small">
        Dùng để kiểm phần chặn <code>h_BUKRS</code>: lấy token của một tài khoản
        không phải <code>SUPER</code> rồi gọi biểu mẫu với đơn vị chưa được gán — phải nhận 403.
      </p>
    </div>
  )
}

// ---------------------------------------------------------------- bieu mau

function BieuMauBox(
  { token, busy, onRun, orgs, myScope, lastFile }: {
    token: string | null
    busy: boolean
    onRun: (fn: () => Promise<ProbeResult>, autoDownload?: boolean) => void
    orgs: OrgItem[]
    /** Pham vi don vi cua token dang dung. null = SUPER (khong gioi han). */
    myScope: string[] | null
    /** File cua lan goi truoc — de nut "upload lai file vua tai" dung. */
    lastFile: { name: string; size: number; url: string } | null
  },
) {
  const [formCode, setFormCode] = useState('KH18')
  const [connId, setConnId] = useState('PB9')
  const [header, setHeader] = useState<Record<string, string>>({
    BUKRS: '',
    YEAR: String(new Date().getFullYear()),
    // Dien san thang hien tai, cung logic voi YEAR. Bieu mau theo nam thi xoa o nay di.
    PERIOD: String(new Date().getMonth() + 1),
    DAY: '', WERKS: '',
  })
  const [file, setFile] = useState<File | null>(null)

  // Danh sach bieu mau doc theo connId. Loi thi de rong -> tu dong rot ve o go tay.
  const [forms, setForms] = useState<BieuMauItem[]>([])
  useEffect(() => {
    let huy = false
    api.listBieuMau(connId.trim() || undefined)
      .then(fs => { if (!huy) setForms(fs) })
      .catch(() => { if (!huy) setForms([]) })
    return () => { huy = true }
  }, [connId])

  // FORM_CODE mac dinh co the khong ton tai o moi truong khac -> keo ve bieu mau dau tien.
  useEffect(() => {
    if (forms.length > 0 && !forms.some(f => f.formCode === formCode))
      setFormCode(forms[0].formCode)
  }, [forms])  // eslint-disable-line react-hooks/exhaustive-deps

  // Chi co mot don vi kha dung thi dien san, khoi phai chon.
  const chonDuoc = useMemo(
    () => (myScope === null ? orgs.map(o => o.bukrs) : orgs.map(o => o.bukrs).filter(b => myScope.includes(b))),
    [orgs, myScope])

  useEffect(() => {
    if (!header.BUKRS && chonDuoc.length === 1)
      setHeader(h => ({ ...h, BUKRS: chonDuoc[0] }))
  }, [chonDuoc])  // eslint-disable-line react-hooks/exhaustive-deps

  function query() {
    const p = new URLSearchParams()
    if (connId.trim()) p.set('connId', connId.trim())
    for (const c of HEADER_COLS) {
      const v = header[c]?.trim()
      if (v) p.set(`h_${c}`, v)
    }
    return p.toString()
  }

  const base = `/api/bieumau/${encodeURIComponent(formCode.trim())}`
  const dangChon = forms.find(f => f.formCode === formCode)

  /** Gui lai chinh file vua tai ve -> kiem tron vong tai-ve/upload-lai bang mot lan bam. */
  async function uploadLaiFileVuaTai() {
    if (!lastFile) return
    const blob = await fetch(lastFile.url).then(r => r.blob())
    const f = new File([blob], lastFile.name,
      { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
    onRun(() => probe('POST', `${base}/import?${query()}`, { token, formFile: f }))
  }

  return (
    <div className="card pad tester-box">
      <h3>Biểu mẫu — tải template / upload dữ liệu</h3>

      <div className="row">
        <label>FORM_CODE
          {forms.length > 0 ? (
            <select value={formCode} onChange={e => setFormCode(e.target.value)}>
              {forms.map(f => (
                <option key={f.formCode} value={f.formCode}>
                  {f.formCode}{f.tenBieuMau ? ` — ${f.tenBieuMau}` : ''}
                </option>
              ))}
            </select>
          ) : (
            <input value={formCode} onChange={e => setFormCode(e.target.value)} />
          )}
        </label>
        <label>connId
          <input value={connId} onChange={e => setConnId(e.target.value)} />
        </label>
      </div>

      {dangChon && (dangChon.soCotCauHinh === 0 || (dangChon.rowExcel ?? 0) <= 1) && (
        <p className="muted small">
          {dangChon.soCotCauHinh === 0
            ? <>⚠ Biểu mẫu này <b>chưa có dòng nào trong DM_BIEU_MAU_CONFIG</b> — template tải về sẽ trắng.</>
            : <>⚠ Biểu mẫu này <b>chưa khai ROW_EXCEL</b> — dòng 1 sẽ vừa là tiêu đề cột vừa là dữ liệu.</>}
        </p>
      )}

      <div className="row">
        <label>h_BUKRS
          {chonDuoc.length > 0 ? (
            <select value={header.BUKRS} onChange={e => setHeader({ ...header, BUKRS: e.target.value })}>
              <option value="">— chọn đơn vị —</option>
              {orgs.filter(o => chonDuoc.includes(o.bukrs)).map(o => (
                <option key={o.bukrs} value={o.bukrs}>{o.bukrs} — {o.butxt}</option>
              ))}
              {/* Cho go ma NGOAI danh muc de con thu duoc ca 403. */}
              <option value="KHONG_CO_MA_NAY">KHONG_CO_MA_NAY (thử 403)</option>
            </select>
          ) : (
            <input value={header.BUKRS}
                   onChange={e => setHeader({ ...header, BUKRS: e.target.value })} />
          )}
        </label>
        {HEADER_COLS.filter(c => c !== 'BUKRS').map(c => (
          <label key={c}>h_{c}
            <input value={header[c]} onChange={e => setHeader({ ...header, [c]: e.target.value })} />
          </label>
        ))}
      </div>

      <code className="url-preview">GET {base}/export?{query()}</code>

      <div className="row">
        <button className="primary" disabled={busy || !formCode.trim()}
                onClick={() => onRun(
                  () => probe('GET', `${base}/export?${query()}`, { token }), true)}>
          Tải template (GET export)
        </button>
        <span className="muted small">File tự tải về ngay khi nhận được.</span>
      </div>

      <hr />

      <div className="row">
        <label>File Excel đã nhập liệu
          <input type="file" accept=".xlsx,.xlsm"
                 onChange={e => setFile(e.target.files?.[0] ?? null)} />
        </label>
        <button disabled={busy || !file || !formCode.trim()}
                onClick={() => onRun(() =>
                  probe('POST', `${base}/import?${query()}`, { token, formFile: file! }))}>
          Upload (POST import)
        </button>
      </div>

      {lastFile && (
        <div className="row">
          <button disabled={busy || !formCode.trim()} onClick={uploadLaiFileVuaTai}>
            Upload lại file vừa tải ({lastFile.name})
          </button>
          <span className="muted small">
            Kiểm trọn vòng tải-về → upload-lại. <b>Ghi thật vào H_DATA/T_DATA</b> nếu hợp lệ.
          </span>
        </div>
      )}
    </div>
  )
}

// ---------------------------------------------------------------- goi tuy y

const METHODS = ['GET', 'POST', 'PUT', 'DELETE'] as const

function FreeCallBox(
  { token, busy, onRun }: {
    token: string | null
    busy: boolean
    onRun: (fn: () => Promise<ProbeResult>) => void
  },
) {
  const [method, setMethod] = useState<string>('GET')
  const [path, setPath] = useState('/api/auth/me')
  const [body, setBody] = useState('')

  const SAMPLES: Array<[string, string, string]> = [
    ['GET', '/api/auth/me', ''],
    ['GET', '/api/admin/users?page=1&pageSize=5', ''],
    ['GET', '/api/admin/users?unassignedOnly=true', ''],
    ['GET', '/api/admin/orgs', ''],
    ['GET', '/api/bieumau', ''],
    ['GET', '/health', ''],
    ['POST', '/api/admin/users',
      JSON.stringify({ username: 'test_bukrs', password: 'Test@12345',
                       fullName: 'Tai khoan thu', isActive: true,
                       bukrs: [], primaryBukrs: null }, null, 2)],
  ]

  return (
    <div className="card pad tester-box">
      <h3>Gọi endpoint bất kỳ</h3>

      <div className="row">
        <label>Method
          <select value={method} onChange={e => setMethod(e.target.value)}>
            {METHODS.map(m => <option key={m} value={m}>{m}</option>)}
          </select>
        </label>
        <label className="grow">Đường dẫn
          <input value={path} onChange={e => setPath(e.target.value)} />
        </label>
        <button className="primary" disabled={busy || !path.trim()}
                onClick={() => onRun(() => probe(method, path.trim(), { token, jsonBody: body }))}>
          Gọi
        </button>
      </div>

      <label>Body (JSON — để trống nếu không cần)
        <textarea rows={6} value={body} onChange={e => setBody(e.target.value)}
                  placeholder='{ "username": "..." }' />
      </label>

      <div className="samples">
        <span className="muted small">Mẫu nhanh:</span>
        {SAMPLES.map(([m, p]) => (
          <button key={m + p} type="button" className="chip"
                  onClick={() => { setMethod(m); setPath(p)
                                   setBody(SAMPLES.find(s => s[0] === m && s[1] === p)?.[2] ?? '') }}>
            {m} {p}
          </button>
        ))}
      </div>
    </div>
  )
}

// ---------------------------------------------------------------- ket qua

function ResultBox({ r }: { r: ProbeResult }) {
  const cls = r.status >= 500 ? 'err' : r.status >= 400 ? 'warn' : 'ok'
  return (
    <div className="card pad tester-box">
      <h3>
        Kết quả{' '}
        <span className={`status ${cls}`}>{r.status} {r.statusText}</span>
        <span className="muted small"> · {r.ms} ms · {r.contentType || '(không có Content-Type)'}</span>
      </h3>

      {r.file ? (
        <p>
          Nhận được file <b>{r.file.name}</b> ({r.file.size.toLocaleString('vi-VN')} bytes){' '}
          <a className="btn-like" href={r.file.url} download={r.file.name}>Tải xuống</a>
        </p>
      ) : (
        <pre className="response">{r.body || '(không có nội dung)'}</pre>
      )}
    </div>
  )
}
