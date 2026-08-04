/** Hinh dang JSON do ASP.NET Core tra ve (camelCase mac dinh). */

export interface UserInfo {
  id: number
  username: string
  fullName: string | null
  email: string | null
  roles: string[]
  /** null = khong gioi han (vai tro SUPER). [] = chua gan don vi nao. */
  allowedBukrs: string[] | null
}

export interface LoginResponse {
  user: UserInfo
  accessToken: string
  tokenType: string
  expiresIn: number
}

export interface UserListItem {
  id: number
  username: string
  fullName: string | null
  email: string | null
  isActive: boolean
  /** BUKRS gan truc tiep trong PT_USER_ORG, phan tu dau la don vi chinh. */
  bukrs: string[]
  roles: string[]
}

/** Ket qua mot lan goi thu o tab "Thu API" — luon tra ve, khong bao gio nem. */
export interface ProbeResult {
  status: number
  statusText: string
  ms: number
  contentType: string
  /** Noi dung text (JSON da format neu la JSON). Null khi la file nhi phan. */
  body: string | null
  /** File tra ve (vd .xlsx) — de nut Tai xuong dung. */
  file?: { name: string; size: number; url: string }
}

/** Bo loc danh sach nguoi dung. `bukrs` va `unassignedOnly` loai tru nhau. */
export interface UserFilter {
  q: string
  includeInactive: boolean
  /** Ma don vi da gan TRUC TIEP (khong mo rong xuong cay con). Rong = moi don vi. */
  bukrs: string
  /** Chi nguoi dung chua gan don vi nao. Khi bat, `bukrs` bi bo qua. */
  unassignedOnly: boolean
}

/** Mot trang ket qua. `page`/`pageSize` la gia tri backend THUC SU dung (da chuan hoa). */
export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  /** Tong so ban ghi khop dieu kien loc, khong phai so ban ghi trong trang. */
  total: number
  totalPages: number
}

export interface OrgItem {
  id: number
  bukrs: string
  butxt: string
  orgType: string | null
  parentId: number | null
  /** Do sau trong cay, 0 = goc — dung de thut le. */
  level: number
}

/** Mot bieu mau trong DM_BIEU_MAU — du de dung dropdown chon FORM_CODE. */
export interface BieuMauItem {
  formCode: string
  tenBieuMau: string | null
  /** null hoac <=1 = chua khai dong bat dau du lieu; xem README muc gioi han. */
  rowExcel: number | null
  isActive: boolean
  /** So dong trong DM_BIEU_MAU_CONFIG. 0 = chua cau hinh cot nao, export se ra file trong. */
  soCotCauHinh: number
}

export interface CreateUserRequest {
  username: string
  password: string
  fullName?: string | null
  email?: string | null
  isActive: boolean
  bukrs: string[]
  primaryBukrs?: string | null
}

export interface UpdateUserRequest {
  fullName?: string | null
  email?: string | null
  isActive: boolean
}

export interface AssignBukrsRequest {
  bukrs: string[]
  primaryBukrs?: string | null
}
