import axios from 'axios'
import { sessionStore } from './session'

export const apiBase = import.meta.env.VITE_API_BASE_URL || '/api'
export const api = axios.create({ baseURL: apiBase, timeout: 30000 })
let refreshJob = null
api.interceptors.request.use(config => {
  const token = config.setupToken || (!config.skipAuth && sessionStore.get()?.accessToken)
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})
api.interceptors.response.use(response => response, async error => {
  const config = error.config
  if (error.response?.status !== 401 || !config || config.retried || config.skipAuth || config.setupToken) throw error
  const refreshToken = sessionStore.get()?.refreshToken
  if (!refreshToken) { sessionStore.set(null); throw error }
  refreshJob ||= axios.post(`${apiBase}/auth/refresh`, { refreshToken }, { timeout: 30000 }).then(({ data }) => {
    if (sessionStore.get()?.refreshToken !== refreshToken) throw Error('Phiên đăng nhập đã thay đổi.')
    sessionStore.set(data); return data
  }).catch(e => { if (sessionStore.get()?.refreshToken === refreshToken) sessionStore.set(null); throw e }).finally(() => { refreshJob = null })
  await refreshJob
  config.retried = true
  return api(config)
})
export const get = (path, params, signal) => api.get(path, { params, signal }).then(r => r.data)
export const post = (path, body) => api.post(path, body).then(r => r.data)
export const patch = (path, body) => api.patch(path, body).then(r => r.data)
export const remove = path => api.delete(path)
export function errorText(error) {
  if (axios.isCancel(error)) return 'Đã huỷ thao tác.'
  const body = error?.response?.data
  if (body instanceof Blob) return `Không tải được file (${error.response.status}).`
  return body?.error || body?.detail || (body?.errors && Object.values(body.errors).flat().join(' ')) || (error?.response?.status === 403 ? 'Bạn không có quyền thực hiện thao tác này.' : error?.response?.status === 413 ? 'Dung lượng vượt giới hạn máy chủ.' : error?.message === 'Network Error' ? 'Không kết nối được máy chủ. Hãy kiểm tra BE đang chạy.' : error?.message) || 'Thao tác không thành công.'
}
export async function download(path, filename) {
  const response = await api.get(path, { responseType: 'blob', timeout: 600000 })
  const url = URL.createObjectURL(response.data)
  const link = document.createElement('a'); link.href = url; link.download = filename; link.click()
  setTimeout(() => URL.revokeObjectURL(url), 30000)
}
export function upload(path, data, { signal, progress, allowEmpty = false } = {}) {
  const files = data.getAll('file'), limit = path === '/test-cases' ? 64 : 256
  if ((!files.length && !allowEmpty) || files.length > 100) throw Error('Chọn từ 1 đến 100 file.')
  if (files.some(f => !f.size)) throw Error('Có file rỗng trong danh sách đã chọn.')
  if (files.reduce((sum, f) => sum + f.size, 0) > limit * 1024 * 1024) throw Error(`Tổng dung lượng vượt ${limit} MB.`)
  return api.post(path, data, { signal, timeout: 600000, onUploadProgress: e => progress?.(e.progress ? Math.round(e.progress * 100) : 0) }).then(r => r.data)
}
