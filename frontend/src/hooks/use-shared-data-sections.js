import { useLocation } from 'react-router-dom'
import { useAuth } from '../context/auth-context'
import { useApi } from './use-api'

const links = [
  { path: '/testcases', label: 'Testcase', permission: 'TESTCASE.VIEW' },
  { path: '/software', label: 'Phiên bản phần mềm', permission: 'DULIEU.VIEW' },
  { path: '/packages', label: 'Gói / cấu hình', permission: 'TESTCASE.VIEW' },
]

export function useSharedDataSections() {
  const { can } = useAuth()
  const { pathname, search } = useLocation()
  const category = new URLSearchParams(search).get('category')
  const categories = useApi('/du-lieu-chung/muc', 'DULIEU.VIEW', undefined, { staleTime: 30000 })
  const database = useApi(
    '/database/files',
    'DULIEU.VIEW',
    { page: 1, size: 1 },
    { staleTime: 30000 },
  )
  const sections = links
    .filter((link) => can(link.permission))
    .map((link) => ({
      ...link,
      active:
        pathname === link.path ||
        (link.path === '/software' && pathname === '/files' && category === 'phien-ban'),
    }))

  if (can('DULIEU.VIEW')) {
    sections.push(
      ...(categories.data || [])
        .filter((item) => item.ma !== 'phien-ban')
        .map((item) => ({
          path: '/files?category=' + encodeURIComponent(item.ma),
          label: item.ten,
          count: item.ma === 'dbc' ? database.data?.total || 0 : item.soFile,
          active: pathname === '/files' && category === item.ma,
        })),
    )
  }

  return { sections, active: pathname === '/files' || links.some((link) => pathname === link.path) }
}
