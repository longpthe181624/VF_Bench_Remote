import { useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import {
  Boxes,
  ClipboardList,
  FolderOpen,
  FolderLock,
  FileCheck,
  Users,
  Shield,
  Bell,
  Settings,
  LogOut,
  Menu,
  X,
  Layers,
  ChevronDown,
  KeyRound,
} from 'lucide-react'
import { useAuth } from '../context/auth-context'
import { useSharedDataSections } from '../hooks/use-shared-data-sections'
import { Button } from './ui'
import { PageErrorBoundary } from './PageErrorBoundary'
const groups = [
  {
    name: 'VẬN HÀNH',
    items: [
      ['/requests', 'Request test', ClipboardList, 'REQUEST.VIEW'],
      ['/devices', 'Thiết bị', Boxes, 'BENCH.VIEW'],
      ['/projects', 'Dự án', Layers, 'BENCH.VIEW'],
      ['/alerts', 'Cảnh báo', Bell, 'BENCH.VIEW'],
    ],
  },
  {
    name: 'DỮ LIỆU',
    items: [
      ['/files', 'Dữ liệu chung', FolderOpen, 'SharedData'],
      ['/storage', 'Kho cá nhân', FolderLock, 'KHO.VIEW'],
      ['/reports', 'Báo cáo', FileCheck, 'REPORT.VIEW'],
    ],
  },
  {
    name: 'QUẢN TRỊ',
    items: [
      ['/users', 'Người dùng', Users, 'USER.VIEW'],
      ['/roles', 'Vai trò', Shield, 'ROLE.VIEW'],
      ['/client-keys', 'API key cho Client', KeyRound, 'AdminOnly'],
    ],
  },
]
export default function AppShell() {
  const { user, can, logout } = useAuth()
  const location = useLocation()
  const navigate = useNavigate()
  const [mobile, setMobile] = useState(false)
  const { sections, active: sharedActive } = useSharedDataSections()
  const [sharedOpen, setSharedOpen] = useState(sharedActive)
  const item = [
    ['/software-types', 'Type phần mềm'],
    ['/database-settings', 'Danh mục File DBC'],
    ...sections.map((section) => [section.path, section.label]),
    ...groups.flatMap((g) => g.items),
  ].find((i) => location.pathname === i[0] || location.pathname.startsWith(i[0] + '/'))
  function sharedMenu() {
    return (
      <>
        <button
          type="button"
          className={'nav-item nav-toggle' + (sharedActive ? ' active' : '')}
          aria-expanded={sharedOpen}
          aria-controls="shared-data-menu"
          onClick={() => setSharedOpen((open) => !open)}
        >
          <FolderOpen size={16} />
          Dữ liệu chung
          <ChevronDown size={14} className={sharedOpen ? 'nav-chevron open' : 'nav-chevron'} />
        </button>
        <div id="shared-data-menu" hidden={!sharedOpen}>
          {sections.map((section) => (
            <NavLink
              key={section.path}
              to={section.path}
              onClick={() => setMobile(false)}
              className={() => 'nav-category' + (section.active ? ' active' : '')}
            >
              {section.label}
              {section.count !== undefined && <span>{section.count}</span>}
            </NavLink>
          ))}
        </div>
      </>
    )
  }
  return (
    <div className="app-shell">
      <aside className={mobile ? 'sidebar mobile-open' : 'sidebar'}>
        <NavLink to="/devices" className="brand">
          <div className="brand-icon">
            <Boxes size={19} />
          </div>
          <div>
            <strong>Bench Console</strong>
            <small>Remote Testing</small>
          </div>
        </NavLink>
        <Button
          variant="ghost"
          className="mobile-close"
          onClick={() => setMobile(false)}
          aria-label="Đóng menu"
        >
          <X size={18} />
        </Button>
        <nav>
          {groups.map((group) => {
            const items = group.items.filter((i) =>
              i[3] === 'SharedData' ? sections.length > 0 : i[3] === 'AdminOnly' ? user.vaiTro.includes('Admin') : can(i[3]),
            )
            return (
              !!items.length && (
                <section className="nav-group" key={group.name}>
                  <h3>{group.name}</h3>
                  {items.map(([path, label, Icon, permission]) => (
                    <div key={path}>
                      {permission === 'SharedData' ? (
                        sharedMenu()
                      ) : (
                        <NavLink
                          to={path}
                          onClick={() => setMobile(false)}
                          className={({ isActive }) => 'nav-item' + (isActive ? ' active' : '')}
                        >
                          <Icon size={16} />
                          {label}
                        </NavLink>
                      )}
                    </div>
                  ))}
                </section>
              )
            )
          })}
        </nav>
        <div className="sidebar-footer">
          <span className="status-dot" />
          Nền tảng kiểm thử nội bộ
        </div>
      </aside>
      {mobile && (
        <button
          className="mobile-backdrop"
          aria-label="Đóng menu"
          onClick={() => setMobile(false)}
        />
      )}
      <div className="main-shell">
        <header className="topbar">
          <Button
            variant="ghost"
            className="mobile-menu"
            aria-label="Mở menu"
            onClick={() => setMobile(true)}
          >
            <Menu size={18} />
          </Button>
          <h1>{item?.[1] || 'Tài khoản'}</h1>
          <div className="topbar-right">
            <div className="profile">
              <strong>{user.hoTen}</strong>
              <small>{user.vaiTro.join(' · ')}</small>
            </div>
            <Button
              variant="ghost"
              aria-label="Tài khoản và bảo mật"
              onClick={() => navigate('/account')}
            >
              <Settings size={17} />
            </Button>
            <Button variant="ghost" onClick={logout}>
              <LogOut size={15} />
              <span className="logout-label">Đăng xuất</span>
            </Button>
          </div>
        </header>
        <main className="page-content">
          <PageErrorBoundary resetKey={location.pathname + location.search}>
            <Outlet />
          </PageErrorBoundary>
        </main>
      </div>
    </div>
  )
}
