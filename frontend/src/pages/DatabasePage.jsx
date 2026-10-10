import { DATABASE_FIELDS } from '../lib/database-fields'
import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { Upload } from 'lucide-react'
import { useAuth } from '../context/auth-context'
import { useApi } from '../hooks/use-api'
import { remove, errorText } from '../services/api'
import { DownloadButton } from '../components/files/DownloadButton'
import { bytes } from '../lib/format'
import { notify } from '../lib/notify'
import {
  Button,
  Badge,
  DataTable,
  ErrorMessage,
  Notice,
  PageHeading,
  Toolbar,
} from '../components/ui'
import {
  DatabaseUpload,
  DatabaseImport,
  DatabaseDetail,
} from '../components/files/DatabaseFileDialogs'
import { FileStatusDialog } from '../components/files/FileStatusDialog'
export default function DatabasePage({ embedded = false }) {
  const { can } = useAuth()
  const cache = useQueryClient()
  const [params, setParams] = useSearchParams()
  const [uploading, setUploading] = useState(false)
  const [importing, setImporting] = useState(false)
  const [selected, setSelected] = useState(null)
  const [statusFile, setStatusFile] = useState(null)
  const [error, setError] = useState('')
  const page = Math.max(1, Number(params.get('page')) || 1)
  const filters = Object.fromEntries(
    ['modelId', 'categoryId', 'typeId', 'status', 'q'].map((k) => [k, params.get(k) || undefined]),
  )
  const lookups = useApi('/database/lookups', 'DULIEU.VIEW')
  const query = useApi(
    '/database/files',
    'DULIEU.VIEW',
    { ...filters, page, size: 20 },
    { refetchInterval: 10000 },
  )
  const update = (key, value) => {
    const next = new URLSearchParams(params)
    next.delete('page')
    value ? next.set(key, value) : next.delete(key)
    setParams(next, { replace: true })
  }
  async function deleteFile(file) {
    if (!window.confirm(`Xoá bản Draft ${file.tenFile} · ${file.phienBan}?`)) return
    try {
      await remove(`/database/files/${file.id}?revision=${file.revision}`)
      notify('Đã xoá bản file.')
    } catch (e) {
      setError(errorText(e))
    } finally {
      await cache.invalidateQueries()
    }
  }
  return (
    <>
      <PageHeading
        title={embedded ? 'File DBC' : 'Database'}
        description="Phân loại theo Model / Category / Type và chọn trạng thái Release hoặc Draft cho từng bản file."
      ></PageHeading>
      <Toolbar
        value={filters.q || ''}
        onSearch={(v) => update('q', v)}
        placeholder="Tìm tên file / phiên bản"
        actions={
          can('DULIEU.UPLOAD') && (
            <>
              <Button disabled={!lookups.data} onClick={() => setImporting(true)}>
                Nhập file đã lưu
              </Button>
              <Button variant="primary" disabled={!lookups.data} onClick={() => setUploading(true)}>
                <Upload size={14} />
                Upload
              </Button>
            </>
          )
        }
      >
        {DATABASE_FIELDS.map(({ key, label, field }) => (
          <select
            key={key}
            aria-label={label}
            value={filters[field] || ''}
            onChange={(e) => update(field, e.target.value)}
          >
            <option value="">ALL — Tất cả {label}</option>
            {(lookups.data?.[key] || []).map((item) => (
              <option key={item.id} value={item.id}>
                {item.ten}
              </option>
            ))}
          </select>
        ))}
        <select
          aria-label="Status"
          value={filters.status || ''}
          onChange={(e) => update('status', e.target.value)}
        >
          <option value="">Mọi Status</option>
          <option value="Release">Release</option>
          <option value="Draft">Draft</option>
        </select>
      </Toolbar>
      <ErrorMessage>
        {error ||
          (query.error && errorText(query.error)) ||
          (lookups.error && errorText(lookups.error))}
      </ErrorMessage>
      <DataTable
        footer={false}
        rows={query.data?.items || []}
        loading={query.isLoading}
        onRow={(f) => setSelected(f.id)}
        columns={[
          { key: 'model', label: 'Model' },
          { key: 'category', label: 'Category' },
          { key: 'type', label: 'Type' },
          {
            key: 'tenFile',
            label: 'Tên file',
            render: (f) => (
              <>
                <strong>{f.tenFile}</strong>
                <small>
                  ID {f.id} · Phiên bản {f.phienBan} · {bytes(f.kichThuoc)}
                </small>
              </>
            ),
          },
          {
            key: 'status',
            label: 'Status',
            render: (f) =>
              can('DATABASE.RELEASE') ? (
                <Button
                  aria-label={`Đổi trạng thái ${f.tenFile} ${f.phienBan}`}
                  className="btn-status"
                  onClick={() => setStatusFile(f)}
                >
                  <Badge tone={f.status === 'Release' ? 'success' : 'warning'}>{f.status}</Badge>
                </Button>
              ) : (
                <Badge tone={f.status === 'Release' ? 'success' : 'warning'}>{f.status}</Badge>
              ),
          },
          {
            key: 'actions',
            label: 'Thao tác',
            render: (f) => (
              <div className="actions">
                <DownloadButton kind="database" file={f} onError={setError} />
                {can('DULIEU.DELETE') && f.status === 'Draft' && (
                  <Button variant="danger" onClick={() => deleteFile(f)}>
                    Xoá
                  </Button>
                )}
              </div>
            ),
          },
        ]}
      />
      <div className="table-footer">
        <span>{query.data?.total || 0} bản file</span>
        <div className="flex gap-2 items-center">
          <Button
            disabled={page === 1}
            onClick={() => {
              const n = new URLSearchParams(params)
              n.set('page', page - 1)
              setParams(n)
            }}
          >
            Trước
          </Button>
          {page} / {Math.max(1, Math.ceil((query.data?.total || 0) / 20))}
          <Button
            disabled={page * 20 >= (query.data?.total || 0)}
            onClick={() => {
              const n = new URLSearchParams(params)
              n.set('page', page + 1)
              setParams(n)
            }}
          >
            Sau
          </Button>
        </div>
      </div>
      <Notice>
        Nhiều bản Release có thể cùng tồn tại. Chọn phiên bản cụ thể cho mỗi lượt kiểm thử.
      </Notice>
      {importing && <DatabaseImport lookups={lookups.data} onClose={() => setImporting(false)} />}{' '}
      {uploading && <DatabaseUpload lookups={lookups.data} onClose={() => setUploading(false)} />}{' '}
      {selected && (
        <DatabaseDetail
          id={selected}
          lookups={lookups.data}
          onClose={() => setSelected(null)}
          onStatus={setStatusFile}
        />
      )}{' '}
      {statusFile && (
        <FileStatusDialog kind="database" file={statusFile} onClose={() => setStatusFile(null)} />
      )}
    </>
  )
}
