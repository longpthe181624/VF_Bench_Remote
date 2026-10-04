import { DATABASE_FIELDS } from '../../lib/database-fields'
import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Upload, Download } from 'lucide-react'
import { useAuth } from '../../context/auth-context'
import { useApi } from '../../hooks/use-api'
import { upload, download, post, errorText } from '../../services/api'
import { fileDownloadPath, updateDraftFile } from '../../services/files'
import { date, bytes } from '../../lib/format'
import { notify } from '../../lib/notify'
import { Button, DataTable, DetailList, ErrorMessage, Field, Modal, Notice } from '../ui'
import { MutationForm } from '../MutationForm'
import { ClassificationSelect } from '../ClassificationSelect'
function Classification({ lookups, file = {} }) {
  return (
    <div className="form-grid">
      {DATABASE_FIELDS.map(({ key, label, field }) => (
        <ClassificationSelect
          key={key}
          label={label}
          name={field}
          required
          items={lookups?.[key] || []}
          defaultValue={file[field]}
          path={'/database/lookups/' + key}
          withCode
        />
      ))}
    </div>
  )
}
export function DatabaseUpload({ lookups, onClose }) {
  const cache = useQueryClient()
  const [busy, setBusy] = useState(false)
  const [progress, setProgress] = useState(0)
  const [error, setError] = useState('')
  const [controller] = useState(() => new AbortController())
  async function save(e) {
    e.preventDefault()
    const data = new FormData(e.currentTarget)
    setBusy(true)
    setError('')
    try {
      await upload('/database/files', data, { signal: controller.signal, progress: setProgress })
      await cache.invalidateQueries()
      notify('Đã lưu bản file Draft.')
      onClose()
    } catch (e) {
      setError(errorText(e))
    } finally {
      setBusy(false)
    }
  }
  const close = () => {
    controller.abort()
    onClose()
  }
  return (
    <Modal open title="Upload Database" onClose={close}>
      <form onSubmit={save}>
        <fieldset disabled={busy} className="form-fields">
          <Classification lookups={lookups} />
          <Field label="Phiên bản *">
            <input name="phienBan" required maxLength={64} placeholder="Ví dụ: 1.0, NP_11.6.4" />
          </Field>
          <Field
            label="Tên file *"
            hint="Một file tối đa 256 MB. Phiên bản mới tạo bản ghi riêng, giữ bản cũ."
          >
            <input name="file" type="file" required />
          </Field>
          <Field label="Mô tả">
            <textarea name="moTa" maxLength={512} rows={3} />
          </Field>
          <Notice>File mới có trạng thái Draft. Người có quyền Release sẽ duyệt sau.</Notice>
        </fieldset>
        <ErrorMessage>{error}</ErrorMessage>
        {busy && (
          <div className="upload-progress">
            <progress max="100" value={progress} />
            <span>{progress === 100 ? 'Đang lưu file…' : progress + '%'}</span>
          </div>
        )}
        <div className="form-footer">
          <Button onClick={close}>{busy ? 'Huỷ tải' : 'Huỷ'}</Button>
          <Button type="submit" variant="primary" busy={busy}>
            <Upload size={14} />
            Tải lên
          </Button>
        </div>
      </form>
    </Modal>
  )
}
export function DatabaseImport({ lookups, onClose }) {
  const cache = useQueryClient()
  const source = useApi('/du-lieu-chung', 'DULIEU.VIEW')
  return (
    <Modal open title="Nhập file từ dữ liệu chung" onClose={onClose}>
      <ErrorMessage>{source.error && errorText(source.error)}</ErrorMessage>
      <MutationForm
        onClose={onClose}
        label="Nhập vào Database"
        onSave={async (form) => {
          await post('/database/import-shared', {
            fileId: Number(form.get('fileId')),
            modelId: Number(form.get('modelId')),
            categoryId: Number(form.get('categoryId')),
            typeId: Number(form.get('typeId')),
            phienBan: form.get('phienBan'),
            moTa: form.get('moTa'),
          })
          await cache.invalidateQueries()
          notify('Đã nhập bản Draft. File nguồn vẫn được giữ.')
        }}
      >
        <Field label="File nguồn *">
          <select name="fileId" required>
            <option value="">Chọn file đã lưu</option>
            {(source.data || []).map((f) => (
              <option value={f.id} key={f.id}>
                {f.tenFile} · {f.ten}
              </option>
            ))}
          </select>
        </Field>
        <Classification lookups={lookups} />
        <Field label="Phiên bản *">
          <input name="phienBan" required maxLength={64} />
        </Field>
        <Field label="Mô tả">
          <textarea name="moTa" maxLength={512} />
        </Field>
        <Notice>
          Chọn phân loại và phiên bản cho file cũ. File nhập có trạng thái Draft; dữ liệu chung được
          giữ nguyên.
        </Notice>
      </MutationForm>
    </Modal>
  )
}
export function DatabaseDetail({ id, lookups, onClose, onStatus }) {
  const { can } = useAuth()
  const cache = useQueryClient()
  const query = useApi(`/database/files/${id}`, 'DULIEU.VIEW')
  const history = useApi(`/database/files/${id}/history`, 'DULIEU.VIEW')
  const [editing, setEditing] = useState(false)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const file = query.data
  return (
    <Modal open wide title={file?.tenFile || 'Chi tiết Database'} onClose={onClose}>
      <ErrorMessage>{error || (query.error && errorText(query.error))}</ErrorMessage>
      {file && (
        <>
          <DetailList
            items={[
              ['ID', file.id],
              ['Phiên bản', file.phienBan],
              ['Model', file.model],
              ['Category', file.category],
              ['Type', file.type],
              ['Status', file.status],
              ['Dung lượng', bytes(file.kichThuoc)],
              [
                'SHA-256',
                <code key="sha" className="break-all">
                  {file.sha256}
                </code>,
              ],
              ['Người upload', file.nguoiTaiLen],
              ['Upload lúc', date(file.taiLenLuc)],
              ['Mô tả', file.moTa],
            ]}
          />
          <div className="flex flex-wrap gap-2 my-5">
            <Button
              busy={busy}
              onClick={async () => {
                setBusy(true)
                try {
                  await download(fileDownloadPath('database', file), file.tenFile)
                } catch (e) {
                  setError(errorText(e))
                } finally {
                  setBusy(false)
                }
              }}
            >
              <Download size={14} />
              Tải về
            </Button>
            {can('DATABASE.RELEASE') && (
              <Button onClick={() => onStatus(file)}>Đổi trạng thái</Button>
            )}
            {file.status === 'Draft' && can('DULIEU.UPLOAD') && (
              <Button onClick={() => setEditing((v) => !v)}>Chỉnh sửa file</Button>
            )}
          </div>
          {editing && file.status === 'Draft' && (
            <MutationForm
              key={file.revision}
              onClose={() => setEditing(false)}
              onSave={async (form) => {
                try {
                  await updateDraftFile('database', file, form)
                } finally {
                  await cache.invalidateQueries()
                }
              }}
            >
              <Classification lookups={lookups} file={file} />
              <Field label="Phiên bản *">
                <input name="phienBan" required maxLength={64} defaultValue={file.phienBan} />
              </Field>
              <Field
                label="Thay file"
                hint="Để trống để giữ nội dung hiện tại. Chỉ bản Draft được cập nhật."
              >
                <input name="file" type="file" />
              </Field>
              <Field label="Mô tả">
                <textarea name="moTa" defaultValue={file.moTa || ''} maxLength={512} />
              </Field>
            </MutationForm>
          )}
          <h3 className="mt-6">Lịch sử thay đổi</h3>
          <ErrorMessage>{history.error && errorText(history.error)}</ErrorMessage>
          <DataTable
            rows={history.data || []}
            loading={history.isLoading}
            columns={[
              {
                key: 'action',
                label: 'Thao tác',
                render: (r) =>
                  ({
                    upload: 'Upload',
                    status: 'Đổi trạng thái',
                    metadata: 'Sửa thông tin',
                    replace: 'Thay file Draft',
                  })[r.action] || r.action,
              },
              {
                key: 'toStatus',
                label: 'Trạng thái',
                render: (r) => `${r.fromStatus ? r.fromStatus + ' → ' : ''}${r.toStatus || ''}`,
              },
              { key: 'nguoiThayDoi', label: 'Người thay đổi' },
              { key: 'thayDoiLuc', label: 'Thời gian', render: (r) => date(r.thayDoiLuc) },
            ]}
          />
        </>
      )}
    </Modal>
  )
}
