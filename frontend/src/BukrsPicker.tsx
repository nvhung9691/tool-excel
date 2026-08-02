import { useMemo, useState } from 'react'
import type { OrgItem } from './types'

interface Props {
  orgs: OrgItem[]
  selected: string[]
  primary: string | null
  onChange: (selected: string[], primary: string | null) => void
}

/**
 * Chon nhieu BUKRS + danh dau 1 don vi chinh (IS_PRIMARY='Y').
 * Bo chon don vi dang la chinh -> tu doi don vi con lai dau tien lam chinh,
 * de khong bao gio gui len danh sach co primary khong nam trong danh sach (backend se tu choi).
 */
export function BukrsPicker({ orgs, selected, primary, onChange }: Props) {
  const [filter, setFilter] = useState('')

  const shown = useMemo(() => {
    const f = filter.trim().toLowerCase()
    if (!f) return orgs
    return orgs.filter(o =>
      o.bukrs.toLowerCase().includes(f) || o.butxt.toLowerCase().includes(f))
  }, [orgs, filter])

  function toggle(bukrs: string) {
    if (selected.includes(bukrs)) {
      const next = selected.filter(b => b !== bukrs)
      onChange(next, primary === bukrs ? (next[0] ?? null) : primary)
    } else {
      const next = [...selected, bukrs]
      onChange(next, primary ?? bukrs)
    }
  }

  return (
    <div className="picker">
      <div className="picker-head">
        <input placeholder="Tìm mã hoặc tên đơn vị…" value={filter}
               onChange={e => setFilter(e.target.value)} />
        <span className="muted">Đã chọn {selected.length}</span>
      </div>

      {orgs.length === 0 && (
        <p className="muted pad">
          Danh mục đơn vị chuẩn (<code>T001</code> của APEX) không trả về bản ghi nào.
        </p>
      )}

      <div className="picker-list">
        {shown.map(o => {
          const checked = selected.includes(o.bukrs)
          return (
            <div key={o.bukrs} className={'picker-row' + (checked ? ' on' : '')}>
              <label style={{ paddingLeft: `${o.level * 16}px` }}>
                <input type="checkbox" checked={checked} onChange={() => toggle(o.bukrs)} />
                <b>{o.bukrs}</b>
                <span className="muted"> — {o.butxt}</span>
              </label>

              <label className={'primary-pick' + (checked ? '' : ' hidden')}
                     title="Đặt làm đơn vị chính">
                <input type="radio" name="primaryBukrs" checked={primary === o.bukrs}
                       onChange={() => onChange(selected, o.bukrs)} />
                chính
              </label>
            </div>
          )
        })}
      </div>
    </div>
  )
}
