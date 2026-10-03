import { Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { useAuth } from './context/auth-context'
import AppShell from './components/AppShell'
import { Toast } from './components/Toast'
import { Notice } from './components/ui'
import LoginPage from './pages/LoginPage'
import DevicesPage, { DeviceDetailPage } from './pages/DevicesPage'
import FilesPage, { CategoriesPage } from './pages/FilesPage'
import RequestsPage from './pages/RequestsPage'
import { ProjectsPage, AlertsPage, ReportsPage } from './pages/OperationsPages'
import { UsersPage, RolesPage } from './pages/AdminPages'
import AccountPage from './pages/AccountPage'
import DatabasePage, { DatabaseSettingsPage } from './pages/DatabasePage'
function Protected(){const {ready,session}=useAuth();return !ready?<div className="auth-page">Đang kiểm tra phiên đăng nhập…</div>:session?<Outlet/>:<Navigate to="/login" replace/>}
function Allowed({permission,children}){const {can}=useAuth();return can(permission)?children:<Notice>Bạn không có quyền truy cập màn này.</Notice>}
export default function App(){return <><Routes><Route path="/login" element={<LoginPage/>}/><Route element={<Protected/>}><Route element={<AppShell/>}><Route index element={<Navigate to="/devices" replace/>}/><Route path="devices" element={<Allowed permission="BENCH.VIEW"><DevicesPage/></Allowed>}/><Route path="devices/:code" element={<Allowed permission="BENCH.VIEW"><DeviceDetailPage/></Allowed>}/><Route path="requests" element={<Allowed permission="BENCH.VIEW"><RequestsPage/></Allowed>}/><Route path="requests/:requestId" element={<Allowed permission="BENCH.VIEW"><RequestsPage/></Allowed>}/><Route path="projects" element={<Allowed permission="BENCH.VIEW"><ProjectsPage/></Allowed>}/><Route path="alerts" element={<Allowed permission="BENCH.VIEW"><AlertsPage/></Allowed>}/><Route path="testcases" element={<Allowed permission="TESTCASE.VIEW"><FilesPage key="testcases" kind="package" onlyTestcase/></Allowed>}/><Route path="packages" element={<Allowed permission="TESTCASE.VIEW"><FilesPage key="packages" kind="package"/></Allowed>}/><Route path="database" element={<Allowed permission="DULIEU.VIEW"><DatabasePage/></Allowed>}/><Route path="database-settings" element={<AdminOnly><DatabaseSettingsPage/></AdminOnly>}/><Route path="software" element={<Allowed permission="DULIEU.VIEW"><FilesPage key="software" software/></Allowed>}/><Route path="files" element={<Allowed permission="DULIEU.VIEW"><FilesPage key="shared"/></Allowed>}/><Route path="categories" element={<Allowed permission="DULIEU.MUC"><CategoriesPage/></Allowed>}/><Route path="storage" element={<Allowed permission="KHO.VIEW"><FilesPage key="private" kind="private"/></Allowed>}/><Route path="reports" element={<Allowed permission="REPORT.VIEW"><ReportsPage/></Allowed>}/><Route path="users" element={<Allowed permission="USER.VIEW"><UsersPage/></Allowed>}/><Route path="roles" element={<Allowed permission="ROLE.VIEW"><RolesPage/></Allowed>}/><Route path="account" element={<AccountPage/>}/><Route path="*" element={<Notice>Không tìm thấy màn yêu cầu.</Notice>}/></Route></Route></Routes><Toast/></>}

function AdminOnly({children}){const {user}=useAuth();return user.vaiTro.includes('Admin')?children:<Notice>Chỉ Admin được cấu hình danh mục Database.</Notice>}
