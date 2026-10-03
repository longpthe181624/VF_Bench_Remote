import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../context/auth-context'
import { post, patch, remove, errorText } from '../services/api'
import { Button, Field, ErrorMessage } from './ui'

export function ClassificationSelect({label,name,items=[],defaultValue='',required=false,disabled=false,loading=false,path,withCode=false}){
  const {user}=useAuth(),cache=useQueryClient()
  const [value,setValue]=useState(String(defaultValue||'')),[editing,setEditing]=useState(null),[title,setTitle]=useState(''),[code,setCode]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState('')
  const selected=items.find(item=>String(item.id)===value),admin=user.vaiTro.includes('Admin')&&!disabled
  async function save(){
    setError('')
    if(!title.trim())return setError('Nhập tên '+label+'.')
    if(withCode&&editing==='add'&&!/^[A-Za-z0-9_-]{1,32}$/.test(code.trim()))return setError('Mã cần 1–32 ký tự: chữ, số, dấu gạch ngang hoặc gạch dưới.')
    setBusy(true)
    try{
      const body={ten:title.trim(),...(withCode?{ma:editing==='add'?code.trim():selected.ma}:{})}
      const item=editing==='add'?await post(path,body):await patch(`${path}/${value}`,body)
      await cache.invalidateQueries();setValue(String(item.id));setEditing(null)
    }catch(e){setError(errorText(e))}finally{setBusy(false)}
  }
  async function change(next){
    setError('')
    if(next==='add'||next==='rename'){setEditing(next);setTitle(next==='rename'?selected?.ten||'':'');setCode('');return}
    if(next==='delete'){
      if(!window.confirm(`Xoá ${selected?.ten}? Danh mục đang được file sử dụng sẽ không được xoá.`))return
      setBusy(true)
      try{await remove(`${path}/${value}`);await cache.invalidateQueries();setValue('')}catch(e){setError(errorText(e))}finally{setBusy(false)}
      return
    }
    setValue(next);setEditing(null)
  }
  return <div><Field label={label+(required?' *':'')}><select name={name} value={value} required={required} disabled={disabled} onChange={e=>{if(!busy)change(e.target.value)}} ref={element=>element?.setCustomValidity(editing||busy?'Lưu hoặc huỷ thay đổi '+label+' trước khi lưu file.':'')}><option value="">{loading?'Đang tải…':required?'Chọn '+label:'Chưa phân loại'}</option>{value&&!selected&&<option value={value}>Đang tải lựa chọn…</option>}{items.map(item=><option key={item.id} value={item.id}>{item.ten}{withCode?' · '+item.ma:''}</option>)}{admin&&<optgroup label="Chỉnh sửa ngay tại đây"><option value="add">+ Thêm {label} mới…</option>{selected&&<><option value="rename">Đổi tên {label} đang chọn…</option><option value="delete">Xoá {label} đang chọn…</option></>}</optgroup>}</select></Field>{editing&&<div className="inline-classification"><Field label={'Tên '+label}><input value={title} maxLength={128} onChange={e=>setTitle(e.target.value)} disabled={busy} onKeyDown={e=>{if(e.key==='Enter'){e.preventDefault();save()}}}/></Field>{withCode&&editing==='add'&&<Field label={'Mã '+label}><input value={code} maxLength={32} onChange={e=>setCode(e.target.value)} disabled={busy}/></Field>}{editing==='rename'&&<small>Đổi tên cập nhật cho tất cả file dùng danh mục này.</small>}<div className="flex flex-wrap gap-2 mt-2"><Button busy={busy} onClick={save}>Lưu {label}</Button><Button disabled={busy} onClick={()=>{setEditing(null);setError('')}}>Huỷ {label}</Button></div></div>}<ErrorMessage>{error}</ErrorMessage></div>
}
