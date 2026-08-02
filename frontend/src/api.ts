import type {
  AssignBukrsRequest, CreateUserRequest, LoginResponse, OrgItem, PagedResult,
  ProbeResult, UpdateUserRequest, UserFilter, UserInfo, UserListItem,
} from './types'

const TOKEN_KEY = 'toolexcel.token'

export const getToken = () => localStorage.getItem(TOKEN_KEY)
export const setToken = (t: string) => localStorage.setItem(TOKEN_KEY, t)
export const clearToken = () => localStorage.removeItem(TOKEN_KEY)

/** Loi co status de goi ben ngoai phan biet 401/403/503. */
export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message)
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const token = getToken()
  const headers = new Headers(init.headers)
  if (token) headers.set('Authorization', `Bearer ${token}`)
  if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')

  const res = await fetch(path, { ...init, headers })

  // Token het han / bi thu hoi -> xoa de App dua ve man dang nhap.
  if (res.status === 401) {
    clearToken()
    throw new ApiError(401, 'Phiên đã hết hạn, vui lòng đăng nhập lại.')
  }

  if (!res.ok) {
    throw new ApiError(res.status, await readError(res))
  }

  if (res.status === 204) return undefined as T
  return (await res.json()) as T
}

/** Backend tra { error: "..." }; neu khong phai JSON thi dung status text. */
async function readError(res: Response): Promise<string> {
  try {
    const body = await res.json()
    if (typeof body?.error === 'string') return body.error
    if (typeof body?.title === 'string') return body.title
    return JSON.stringify(body)
  } catch {
    return `Lỗi ${res.status} ${res.statusText}`
  }
}

/**
 * Goi mot endpoint bat ky va tra ve NGUYEN TRANG THAI, khong nem loi.
 *
 * Khac han <c>request()</c> o hai diem co y:
 * - KHONG xoa token khi gap 401. Goi thu bi 401 la ket qua can xem, khong duoc lam
 *   nguoi dang thu bi dang xuat.
 * - Cho phep dung token cua tai khoan KHAC (tham so token) de thu quyen, ma khong
 *   phai dang xuat khoi tai khoan quan tri.
 */
export async function probe(
  method: string,
  path: string,
  opts: { token?: string | null; jsonBody?: string; formFile?: File } = {},
): Promise<ProbeResult> {
  const headers = new Headers()
  const token = opts.token ?? getToken()
  if (token) headers.set('Authorization', `Bearer ${token}`)

  let body: BodyInit | undefined
  if (opts.formFile) {
    const fd = new FormData()
    fd.append('file', opts.formFile)
    body = fd   // KHONG tu dat Content-Type: browser phai tu them boundary
  } else if (opts.jsonBody?.trim()) {
    headers.set('Content-Type', 'application/json')
    body = opts.jsonBody
  }

  const t0 = performance.now()
  const res = await fetch(path, { method, headers, body })
  const ms = Math.round(performance.now() - t0)
  const contentType = res.headers.get('Content-Type') ?? ''

  // Phai kiem CHINH XAC, khong dung tim chuoi con: content type cua .xlsx la
  // "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" — co chua "xml",
  // nen kieu kiem /xml/ se coi file Excel la text va lam mat nut Tai xuong. Da gap that.
  const mime = contentType.split(';')[0].trim().toLowerCase()
  const isText =
    mime === '' ||
    mime.startsWith('text/') ||
    mime === 'application/json' ||
    mime === 'application/problem+json' ||
    mime === 'application/xml' ||
    mime.endsWith('+json') ||
    mime.endsWith('+xml')

  if (isText) {
    const raw = await res.text()
    let pretty = raw
    try { pretty = JSON.stringify(JSON.parse(raw), null, 2) } catch { /* khong phai JSON */ }
    return { status: res.status, statusText: res.statusText, ms, contentType, body: pretty }
  }

  const blob = await res.blob()
  const cd = res.headers.get('Content-Disposition') ?? ''
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(cd)?.[1] ?? 'ketqua.bin'
  return {
    status: res.status, statusText: res.statusText, ms, contentType, body: null,
    file: { name: decodeURIComponent(name), size: blob.size, url: URL.createObjectURL(blob) },
  }
}

/**
 * Kiem hinh dang tra ve co dung la mot trang khong.
 *
 * Ly do co ham nay: neu ban build giao dien cu hon API (git pull ma quen npm run build),
 * API tra { items, page, total } con code cu doi mot mang -> vo bang "e.map is not a function",
 * khong noi len duoc nguyen nhan. Da gap that. Bay o day de bao dung viec can lam.
 */
function expectPage<T>(res: PagedResult<T>): PagedResult<T> {
  if (!res || !Array.isArray(res.items)) {
    throw new ApiError(500,
      'API trả về dạng dữ liệu không mong đợi. Thường là giao diện cũ hơn API: ' +
      'chạy "cd frontend && npm run build" rồi tải lại trang bằng Ctrl+Shift+R.')
  }
  return res
}

export const api = {
  login: (username: string, password: string) =>
    request<LoginResponse>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    }),

  me: () => request<UserInfo>('/api/auth/me'),

  listOrgs: () => request<OrgItem[]>('/api/admin/orgs'),

  listUsers: (f: UserFilter, page: number, pageSize: number) => {
    const p = new URLSearchParams({
      includeInactive: String(f.includeInactive),
      page: String(page),
      pageSize: String(pageSize),
    })
    if (f.q.trim()) p.set('q', f.q.trim())
    // Hai lua chon nay loai tru nhau — cung o mot dropdown ben UI.
    if (f.unassignedOnly) p.set('unassignedOnly', 'true')
    else if (f.bukrs) p.set('bukrs', f.bukrs)
    return request<PagedResult<UserListItem>>(`/api/admin/users?${p}`).then(expectPage)
  },

  createUser: (req: CreateUserRequest) =>
    request<UserListItem>('/api/admin/users', { method: 'POST', body: JSON.stringify(req) }),

  updateUser: (id: number, req: UpdateUserRequest) =>
    request<UserListItem>(`/api/admin/users/${id}`, { method: 'PUT', body: JSON.stringify(req) }),

  changePassword: (id: number, newPassword: string) =>
    request<void>(`/api/admin/users/${id}/password`, {
      method: 'POST',
      body: JSON.stringify({ newPassword }),
    }),

  assignBukrs: (id: number, req: AssignBukrsRequest) =>
    request<UserListItem>(`/api/admin/users/${id}/bukrs`, {
      method: 'PUT',
      body: JSON.stringify(req),
    }),
}
