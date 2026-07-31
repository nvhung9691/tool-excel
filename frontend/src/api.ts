import type {
  AssignBukrsRequest, CreateUserRequest, LoginResponse, OrgItem,
  PagedResult, UpdateUserRequest, UserFilter, UserInfo, UserListItem,
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
