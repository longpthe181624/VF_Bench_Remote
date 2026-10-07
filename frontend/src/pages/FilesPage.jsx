import { useState } from 'react'
import { Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { Upload, FolderCog } from 'lucide-react'
import { useAuth } from '../context/auth-context'
import { useApi } from '../hooks/use-api'
import { post, remove, errorText } from '../services/api'
import { getFileConfig } from '../services/files'
import { notify } from '../lib/notify'
import { bytes, date, matches } from '../lib/format'
import {
  Badge,
  Button,
  DataTable,
  ErrorMessage,
  Field,
  Modal,
  Notice,
  PageHeading,
  Toolbar,
} from '../components/ui'
import { MutationForm } from '../components/MutationForm'
import { DownloadButton } from '../components/files/DownloadButton'
import { FileDetailModal } from '../components/files/FileDetailModal'
import { UploadDialog } from '../components/files/UploadDialog'
import { FileStatusDialog } from '../components/files/FileStatusDialog'
import { SharedDataTabs } from '../components/files/SharedDataTabs'
import { documentFields } from '../lib/document-fields'
import DatabasePage from './DatabasePage'
export default function FilesPage(props) {
  const [params] = useSearchParams()
  if ((props.kind || 'shared') === 'shared' && !props.software && !params.get('category'))
    return <Navigate to="/files?category=tai-lieu" replace />
  if ((props.kind || 'shared') === 'shared' && !props.software && params.get('category') === 'dbc')
    return (
      <>
        <PageHeading
          title="Dữ liệu chung"
          description="Tài liệu và file dùng chung của nhóm, phân theo mục."
        />
        <SharedDataTabs />
        <DatabasePage embedded />
      </>
    )
  return <FilesListPage {...props} />
}
function FilesListPage({ kind = 'shared', software = false, onlyTestcase = false }) {
  const { can } = useAuth()
  const cache = useQueryClient()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const config = getFileConfig(kind)
  const categories = useApi('/du-lieu-chung/muc', 'DULIEU.VIEW')
  const [q, setQ] = useState('')
  const category = software ? 'phien-ban' : params.get('category') || ''
  const softwareTypes = useApi('/software/types', 'DULIEU.VIEW', null, {
    enabled: kind === 'shared' && can('DULIEU.VIEW'),
  })
  const documents = kind === 'shared' && category === 'tai-lieu'
  const documentLookups = useApi('/du-lieu-chung/document-lookups', 'DULIEU.VIEW', null, { enabled: documents && can('DULIEU.VIEW') })
  const documentFilters = documents ? Object.fromEntries(documentFields.map(field => [field.key, params.get(field.key) || undefined])) : {}
  const softwareTypeId = category === 'phien-ban' ? params.get('softwareTypeId') || '' : ''
  const files = useApi(
    config.path,
    config.view,
    kind === 'shared'
      ? { loai: category || undefined, softwareTypeId: softwareTypeId || undefined, ...documentFilters, q: documents && q ? q : undefined }
      : kind === 'package' && onlyTestcase
        ? { loai: 'testcase' }
        : undefined,
  )
  const devices = useApi('/devices', 'BENCH.VIEW', null, {
    enabled: kind === 'package' && can('BENCH.VIEW'),
  })
  const [type, setType] = useState('')
  const [uploading, setUploading] = useState(false)
  const [selected, setSelected] = useState(null)
  const [deploying, setDeploying] = useState(null)
  const [statusFile, setStatusFile] = useState(null)
  const [error, setError] = useState('')
  const canUpload =
    kind === 'package'
      ? can('TESTCASE.UPLOAD') || (!onlyTestcase && can('CONFIG.UPLOAD'))
      : can(config.upload)
  const rows = (files.data || []).filter(
    (f) =>
      matches(f, q, ['ten', 'tenFile', 'tenFileGoc', 'moTa', 'nguoiTaiLen', ...documentFields.map(field => field.key)]) &&
      (!type || f.loai === type),
  )
  async function deleteFile(file) {
    if (!window.confirm(`Xoá ${file.ten || file.tenFile}?`)) return
    try {
      await remove(
        kind === 'private'
          ? `/storage/files/${file.id}`
          : `${config.path}/${file.id}${kind === 'shared' ? '?revision=' + file.revision : ''}`,
      )
      await cache.invalidateQueries()
      notify('Đã xoá file.')
    } catch (e) {
      setError(errorText(e))
    }
  }
  return (
    <>
      <PageHeading
        title={software ? 'Phiên bản phần mềm' : documents ? 'Tài liệu' : onlyTestcase ? 'Testcase' : config.title}
        description={
          software ? 'Kho file phần mềm và ghi chú phiên bản dùng chung.' : documents ? 'Phân loại theo chương trình, domain, function và loại tài liệu.' : config.description
        }
      >
        <Badge>{files.data?.length || 0} file</Badge>
      </PageHeading>
      {(kind === 'shared' || kind === 'package') && <SharedDataTabs />}
      <Toolbar
        value={q}
        onSearch={setQ}
        placeholder={documents ? 'Tìm tên / chương trình / Category / Function / Type' : 'Tìm tên / file / mô tả'}
        actions={
          <>
            {kind === 'shared' && category !== 'phien-ban' && !documents && can('DULIEU.MUC') && (
              <Button onClick={() => navigate('/categories')}>
                <FolderCog size={14} />
                Quản lý mục
              </Button>
            )}
            {canUpload && (
              <Button variant="primary" onClick={() => setUploading(true)}>
                <Upload size={14} />
                Tải lên
              </Button>
            )}
          </>
        }
      >
        {documents && documentFields.map(field => <select key={field.key} aria-label={`Lọc ${field.label}`} value={params.get(field.key) || ''} onChange={e => {
          const next = new URLSearchParams(params)
          e.target.value ? next.set(field.key, e.target.value) : next.delete(field.key)
          setParams(next)
        }}><option value="">Tất cả {field.label}</option>{(documentLookups.data?.[field.key] || []).map(value => <option key={value} value={value}>{value}</option>)}</select>)}
        {category === 'phien-ban' && (
          <select
            aria-label="Type phần mềm"
            value={softwareTypeId}
            onChange={(e) => {
              const next = new URLSearchParams(params)
              e.target.value
                ? next.set('softwareTypeId', e.target.value)
                : next.delete('softwareTypeId')
              setParams(next)
            }}
          >
            <option value="">ALL — Tất cả Type</option>
            <option value="0">Chưa phân loại</option>
            {(softwareTypes.data || []).map((t) => (
              <option key={t.id} value={t.id}>
                {t.ten}
              </option>
            ))}
          </select>
        )}
        {kind === 'package' && !onlyTestcase && (
          <select aria-label="Loại gói" value={type} onChange={(e) => setType(e.target.value)}>
            <option value="">Mọi loại</option>
            <option value="testcase">Testcase</option>
            <option value="config">Cấu hình</option>
          </select>
        )}
      </Toolbar>
      <ErrorMessage>{error || (files.error && errorText(files.error)) || (documentLookups.error && errorText(documentLookups.error))}</ErrorMessage>
      <DataTable
        key={q + type + category + softwareTypeId + JSON.stringify(documentFilters)}
        rows={rows}
        loading={files.isLoading}
        columns={[
          {
            key: 'ten',
            label: kind === 'package' ? 'Tên gói' : 'File',
            render: (f) => (
              <>
                <strong>{f.ten || f.tenFile}</strong>
                {f.ten && <small>{f.tenFile || f.tenFileGoc}</small>}
              </>
            ),
          },
          ...(kind === 'package'
            ? [
                {
                  key: 'loai',
                  label: 'Loại',
                  render: (f) => (
                    <Badge>
                      {f.loai === 'config'
                        ? 'Cấu hình'
                        : f.kieuTest === 'manual'
                          ? 'Manual · Excel'
                          : 'Tự động'}
                    </Badge>
                  ),
                },
                {
                  key: 'soTestCase',
                  label: 'Nội dung',
                  render: (f) =>
                    f.loai === 'config'
                      ? '—'
                      : `${f.soTestCase} ${f.kieuTest === 'manual' ? 'file Excel' : 'testcase'}`,
                },
              ]
            : kind === 'shared'
              ? [{ key: 'tenLoai', label: 'Mục' }]
              : []),
          ...(kind === 'shared' && category === 'phien-ban'
            ? [
                {
                  key: 'status',
                  label: 'Status',
                  render: (f) =>
                    can('DATABASE.RELEASE') ? (
                      <Button
                        aria-label={'Đổi trạng thái ' + f.ten}
                        className="btn-status"
                        onClick={() => setStatusFile(f)}
                      >
                        <Badge tone={f.status === 'Release' ? 'success' : 'warning'}>
                          {f.status}
                        </Badge>
                      </Button>
                    ) : (
                      <Badge>{f.status}</Badge>
                    ),
                },
              ]
            : []),
          ...(kind === 'shared' && (category === 'phien-ban' || !category)
            ? [
                {
                  key: 'softwareType',
                  label: 'Type phần mềm',
                  render: (f) =>
                    f.loai === 'phien-ban' ? f.softwareType || 'Chưa phân loại' : '—',
                },
              ]
            : []),
          ...(documents ? documentFields.map(field => ({ key: field.key, label: field.label, render: file => file[field.key] || '—' })) : []),
          { key: 'kichThuoc', label: 'Dung lượng', render: (f) => bytes(f.kichThuoc) },
          ...(kind !== 'package' ? [{ key: 'moTa', label: 'Mô tả' }] : []),
          ...(kind !== 'private' ? [{ key: 'nguoiTaiLen', label: 'Người tải lên' }] : []),
          { key: 'taiLenLuc', label: 'Tải lên lúc', render: (f) => date(f.taiLenLuc) },
          {
            key: 'actions',
            label: 'Thao tác',
            render: (f) => (
              <div className="actions">
                <DownloadButton file={f} kind={kind} />
                <Button onClick={() => setSelected(f)}>
                  {kind !== 'package' && can(config.upload) && f.status !== 'Release'
                    ? 'Thông tin / sửa'
                    : 'Thông tin'}
                </Button>
                {kind === 'package' &&
                  f.kieuTest !== 'manual' &&
                  can(f.loai === 'config' ? 'CONFIG.DEPLOY' : 'TESTCASE.DEPLOY') && (
                    <Button onClick={() => setDeploying(f)}>Triển khai</Button>
                  )}
                {can(config.delete) && f.status !== 'Release' && (
                  <Button variant="danger" onClick={() => deleteFile(f)}>
                    Xoá
                  </Button>
                )}
              </div>
            ),
          },
        ]}
      />
      {software && (
        <Notice>
          Chọn phiên bản / flash theo Request cần API quản lý phiên bản của BE. Kho này hiện lưu
          file và mô tả phiên bản thực tế.
        </Notice>
      )}
      {uploading && (
        <UploadDialog
          kind={kind}
          category={category}
          categories={categories.data || []}
          onlyTestcase={onlyTestcase}
          onClose={() => setUploading(false)}
        />
      )}{' '}
      {selected && (
        <FileDetailModal
          file={selected}
          kind={kind}
          categories={categories.data || []}
          onClose={() => setSelected(null)}
          onStatus={(f) => {
            setSelected(null)
            setStatusFile(f)
          }}
        />
      )}
      {statusFile && <FileStatusDialog file={statusFile} onClose={() => setStatusFile(null)} />}
      <Modal
        open={!!deploying}
        onClose={() => setDeploying(null)}
        title={`Triển khai ${deploying?.ten || ''}`}
      >
        <MutationForm
          onClose={() => setDeploying(null)}
          onSave={async (form) => {
            const result = await post(`/devices/${encodeURIComponent(form.get('device'))}/deploy`, {
              goiId: deploying.id,
            })
            notify(`Đã gửi lệnh ${result.cmdId}. Đang chờ client xác nhận.`)
            await cache.invalidateQueries()
          }}
          label="Gửi tới client"
        >
          <Field label="Thiết bị nhận">
            <select name="device" required>
              <option value="">Chọn Bench / Vehicle / ECU</option>
              {(devices.data || [])
                .filter((d) => d.hoTroRemote)
                .map((d) => (
                  <option key={d.code} value={d.code}>
                    {d.ten || d.code} · {d.code}
                  </option>
                ))}
            </select>
          </Field>
        </MutationForm>
      </Modal>
    </>
  )
}
