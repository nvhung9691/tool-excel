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

export interface OrgItem {
  id: number
  bukrs: string
  butxt: string
  orgType: string | null
  parentId: number | null
  /** Do sau trong cay, 0 = goc — dung de thut le. */
  level: number
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
