import { useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { Boxes, ClipboardList, FolderOpen, FolderLock, FileArchive, FileCheck, Users, Shield, Bell, Settings, LogOut, Menu, X, Cpu, Layers } from 'lucide-react'
import { useAuth } from '../context/auth-context'
import { useApi } from '../hooks/use-api'
import { Button } from './ui'
const groups=[
  {name:'VẬN HÀNH',items:[['/requests','Request test',ClipboardList,'BENCH.VIEW'],['/devices','Thiết bị',Boxes,'BENCH.VIEW'],['/projects','Dự án',Layers,'BENCH.VIEW'],['/alerts','Cảnh báo',Bell,'BENCH.VIEW']]},
  {name:'DỮ LIỆU',items:[['/database','Database',Layers,'DULIEU.VIEW'],['/testcases','Testcase',FileCheck,'TESTCASE.VIEW'],['/software','Phần mềm',Cpu,'DULIEU.VIEW'],['/packages','Gói / cấu hình',FileArchive,'TESTCASE.VIEW'],['/files','Dữ liệu chung',FolderOpen,'DULIEU.VIEW'],['/storage','Kho cá nhân',FolderLock,'KHO.VIEW'],['/reports','Báo cáo',FileCheck,'REPORT.VIEW']]},
  {name:'QUẢN TRỊ',items:[['/software-types','Type phần mềm',Settings,'AdminOnly'],['/database-settings','Cấu hình Database',Settings,'AdminOnly'],['/users','Người dùng',Users,'USER.VIEW'],['/roles','Vai trò',Shield,'ROLE.VIEW']]},
]
export default function AppShell(){
  const {user,can,logout}=useAuth(),location=useLocation(),navigate=useNavigate()
  const [mobile,setMobile]=useState(false)
  const categories=useApi('/du-lieu-chung/muc','DULIEU.VIEW',{},{staleTime:30000})
  const item=groups.flatMap(g=>g.items).find(i=>location.pathname===i[0]||location.pathname.startsWith(i[0]+'/'))
  return <div className="app-shell"><aside className={mobile?'sidebar mobile-open':'sidebar'}><NavLink to="/devices" className="brand"><div className="brand-icon"><Boxes size={19}/></div><div><strong>Bench Console</strong><small>Remote Testing</small></div></NavLink><Button variant="ghost" className="mobile-close" onClick={()=>setMobile(false)} aria-label="Đóng menu"><X size={18}/></Button><nav>{groups.map(group=>{const items=group.items.filter(i=>i[3]==='AdminOnly'?user.vaiTro.includes('Admin'):can(i[3]));return !!items.length&&<section className="nav-group" key={group.name}><h3>{group.name}</h3>{items.map(([path,label,Icon])=><div key={path}><NavLink to={path} onClick={()=>setMobile(false)} className={({isActive})=>'nav-item'+(isActive?' active':'')}><Icon size={16}/>{label}</NavLink>{path==='/files'&&(categories.data||[]).map(c=><NavLink className="nav-category" onClick={()=>setMobile(false)} to={`/files?category=${encodeURIComponent(c.ma)}`} key={c.ma}>{c.ten}<span>{c.soFile}</span></NavLink>)}</div>)}</section>})}</nav><div className="sidebar-footer"><span className="status-dot"/>Nền tảng kiểm thử nội bộ</div></aside>{mobile&&<button className="mobile-backdrop" aria-label="Đóng menu" onClick={()=>setMobile(false)}/>}<div className="main-shell"><header className="topbar"><Button variant="ghost" className="mobile-menu" aria-label="Mở menu" onClick={()=>setMobile(true)}><Menu size={18}/></Button><h1>{item?.[1]||'Tài khoản'}</h1><div className="topbar-right"><div className="profile"><strong>{user.hoTen}</strong><small>{user.vaiTro.join(' · ')}</small></div><Button variant="ghost" aria-label="Tài khoản và bảo mật" onClick={()=>navigate('/account')}><Settings size={17}/></Button><Button variant="ghost" onClick={logout}><LogOut size={15}/><span className="logout-label">Đăng xuất</span></Button></div></header><main className="page-content"><Outlet/></main></div></div>
}
