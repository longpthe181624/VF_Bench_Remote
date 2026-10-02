import { useState } from 'react'
import { Dialog } from 'radix-ui'
import { X, Search, ChevronLeft, ChevronRight, LoaderCircle } from 'lucide-react'
import { clsx } from 'clsx'
import { twMerge } from 'tailwind-merge'
const cn = (...values) => twMerge(clsx(values))
export function Button({ variant = 'outline', className, busy, children, disabled, ...props }) {
  return <button type="button" className={cn('btn',variant==='primary'&&'btn-primary',variant==='danger'&&'btn-danger',variant==='ghost'&&'btn-ghost',className)} disabled={disabled||busy} {...props}>{busy&&<LoaderCircle size={14} className="animate-spin"/>}{children}</button>
}
export function Field({ label, hint, children, className }) { return <label className={cn('field',className)}><span>{label}</span>{children}{hint&&<small>{hint}</small>}</label> }
export function ErrorMessage({ children }) { return children ? <div role="alert" className="error-box">{children}</div> : null }
export function Notice({ children }) { return <div className="notice">{children}</div> }
export function Badge({ children, tone = 'neutral' }) { return <span className={cn('badge',`badge-${tone}`)}>{children}</span> }
export function Modal({ open, onClose, title, children, wide = false }) {
  return <Dialog.Root open={open} onOpenChange={value=>{if(!value)onClose()}}><Dialog.Portal><Dialog.Overlay className="modal-overlay"/><Dialog.Content className={cn('modal',wide&&'modal-wide')} aria-describedby={undefined}><div className="modal-header"><Dialog.Title>{title}</Dialog.Title><Dialog.Close asChild><Button variant="ghost" aria-label="Đóng"><X size={17}/></Button></Dialog.Close></div><div className="modal-body">{children}</div></Dialog.Content></Dialog.Portal></Dialog.Root>
}
export function Toolbar({ value, onSearch, placeholder='Tìm kiếm', children, actions }) {
  return <div className="toolbar"><label className="search"><Search size={15}/><input aria-label="Tìm kiếm" value={value||''} onChange={e=>onSearch(e.target.value)} placeholder={placeholder}/></label>{children}<div className="toolbar-actions">{actions}</div></div>
}
export function DataTable({ columns, rows = [], keyOf = row=>row.id||row.code||row.ma, onRow, loading, empty='Chưa có dữ liệu.', pageSize=20, footer=true }) {
  const [requestedPage,setPage]=useState(1)
  const max=Math.max(1,Math.ceil(rows.length/pageSize)),page=Math.min(requestedPage,max),start=(page-1)*pageSize
  return <div className="table-card"><div className="table-scroll"><table><thead><tr><th className="row-number">STT</th>{columns.map(c=><th key={c.key}>{c.label}</th>)}</tr></thead><tbody>{loading?<tr><td colSpan={columns.length+1} className="empty"><LoaderCircle size={18} className="animate-spin inline mr-2"/>Đang tải dữ liệu…</td></tr>:!rows.length?<tr><td colSpan={columns.length+1} className="empty">{empty}</td></tr>:rows.slice(start,start+pageSize).map((row,i)=><tr key={keyOf(row)} className={onRow?'clickable-row':''} tabIndex={onRow?0:undefined} aria-label={onRow?`Mở chi tiết ${row.ten||row.code||row.ma}`:undefined} onClick={onRow?e=>{if(!e.target.closest('button,a,input,select,textarea'))onRow(row)}:undefined} onKeyDown={onRow?e=>{if(e.target===e.currentTarget&&['Enter',' '].includes(e.key)){e.preventDefault();onRow(row)}}:undefined}><td className="row-number">{start+i+1}</td>{columns.map(c=><td key={c.key}>{c.render?c.render(row):row[c.key]??'—'}</td>)}</tr>)}</tbody></table></div>{footer&&<div className="table-footer"><span>{rows.length?`${start+1}–${Math.min(start+pageSize,rows.length)} / ${rows.length} bản ghi`:'0 bản ghi'}</span><div className="flex items-center gap-2"><Button aria-label="Trang trước" disabled={page===1} onClick={()=>setPage(page-1)}><ChevronLeft size={14}/></Button><span>{page} / {max}</span><Button aria-label="Trang sau" disabled={page===max} onClick={()=>setPage(page+1)}><ChevronRight size={14}/></Button></div></div>}</div>
}
export function PageHeading({title,description,children}) { return <div className="page-heading"><div><h2>{title}</h2>{description&&<p>{description}</p>}</div><div className="flex flex-wrap gap-2">{children}</div></div> }
export function DetailList({ items }) { return <dl className="detail-list">{items.map(([label,value])=><div key={label}><dt>{label}</dt><dd>{value??'—'}</dd></div>)}</dl> }
