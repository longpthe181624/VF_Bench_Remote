import { patch, upload } from './api'

const configurations = {
  private: {
    title: 'Kho cá nhân',
    description: 'File riêng của bạn. Các tài khoản khác, kể cả Admin, không truy cập được.',
    path: '/storage',
    view: 'KHO.VIEW',
    upload: 'KHO.UPLOAD',
    delete: 'KHO.DELETE',
  },
  shared: {
    title: 'Dữ liệu chung',
    description: 'Tài liệu và file dùng chung của nhóm, phân theo mục.',
    path: '/du-lieu-chung',
    view: 'DULIEU.VIEW',
    upload: 'DULIEU.UPLOAD',
    delete: 'DULIEU.DELETE',
  },
  package: {
    title: 'Gói / cấu hình',
    description: 'Lưu ZIP testcase tự động, Excel manual và cấu hình client.',
    path: '/test-cases',
    view: 'TESTCASE.VIEW',
    upload: 'TESTCASE.UPLOAD',
    delete: 'TESTCASE.DELETE',
  },
}
const draftCollections = { shared: '/du-lieu-chung', database: '/database/files' }

export const getFileConfig = (kind) => configurations[kind] || configurations.shared
export const fileName = (kind, file) => (kind === 'package' ? file.ten + '.zip' : file.tenFile)

export function fileDownloadPath(kind, file) {
  switch (kind) {
    case 'private':
      return `/storage/files/${file.id}/download`
    case 'package':
      return `/test-cases/${file.id}/download`
    case 'report':
      return `/runs/reports/${file.id}/download`
    case 'database':
      return `/database/files/${file.id}/download?revision=${file.revision}`
    default:
      return `/du-lieu-chung/${file.id}/download?revision=${file.revision}`
  }
}

export function updateDraftFile(kind, file, form) {
  form.set('revision', file.revision)
  if (!form.get('file')?.name) form.delete('file')
  return upload(`${draftCollections[kind]}/${file.id}/update`, form, { allowEmpty: true })
}

export function changeFileStatus(kind, file, status) {
  return patch(`${draftCollections[kind]}/${file.id}/status`, { status, revision: file.revision })
}
