import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Upload } from 'lucide-react'
import { useAuth } from '../../context/auth-context'
import { upload, errorText } from '../../services/api'
import { notify } from '../../lib/notify'
import { Button, ErrorMessage, Field, Modal } from '../ui'
import { DocumentFields } from './DocumentFields'
import { SoftwareTypeSelect } from './SoftwareTypeSelect'
export function UploadDialog({ kind, category, categories, onClose, onlyTestcase = false }) {
  const { can } = useAuth()
  const cache = useQueryClient()
  const controller = useRef(null)
  const [busy, setBusy] = useState(false)
  const [progress, setProgress] = useState(0)
  const [error, setError] = useState('')
  const [packageType, setPackageType] = useState(can('TESTCASE.UPLOAD') ? 'testcase' : 'config')
  const [uploadCategory, setUploadCategory] = useState(category || 'khac')
  useEffect(() => () => controller.current?.abort(), [])
  async function save(e) {
    e.preventDefault()
    setBusy(true)
    setError('')
    setProgress(0)
    const form = new FormData(e.currentTarget)
    controller.current = new AbortController()
    try {
      await upload(
        kind === 'private' ? '/storage' : kind === 'package' ? '/test-cases' : '/du-lieu-chung',
        form,
        { signal: controller.current.signal, progress: setProgress },
      )
      await cache.invalidateQueries()
      notify('Đã tải lên và lưu file.')
      onClose()
    } catch (e) {
      setError(errorText(e))
    } finally {
      setBusy(false)
    }
  }
  return (
    <Modal
      open
      onClose={() => {
        controller.current?.abort()
        onClose()
      }}
      title={kind === 'package' ? 'Tải gói ZIP lên' : 'Tải file lên'}
    >
      <form onSubmit={save}>
        <fieldset disabled={busy} className="form-fields">
          {kind === 'shared' && (
            <>
              <Field label="Mục">
                <select
                  name="loai"
                  value={uploadCategory}
                  onChange={(e) => setUploadCategory(e.target.value)}
                >
                  {categories
                    .filter((c) => c.ma !== 'dbc')
                    .map((c) => (
                      <option key={c.ma} value={c.ma}>
                        {c.ten}
                      </option>
                    ))}
                </select>
              </Field>
              <Field label="Tên hiển thị" hint="Để trống để dùng tên file.">
                <input name="ten" maxLength={128} />
              </Field>
            </>
          )}
          {kind === 'shared' && uploadCategory === 'phien-ban' && <SoftwareTypeSelect required />}
          {kind === 'shared' && uploadCategory === 'tai-lieu' && <DocumentFields />}
          {kind === 'package' && (
            <>
              <Field label="Loại gói">
                <select
                  name="loai"
                  value={packageType}
                  onChange={(e) => setPackageType(e.target.value)}
                >
                  {can('TESTCASE.UPLOAD') && <option value="testcase">Testcase</option>}
                  {!onlyTestcase && can('CONFIG.UPLOAD') && (
                    <option value="config">Cấu hình Qauto</option>
                  )}
                </select>
              </Field>
              {packageType === 'testcase' && (
                <Field label="Loại kiểm thử">
                  <select name="kieuTest" defaultValue="auto">
                    <option value="auto">Tự động · .tc / .mtc</option>
                    <option value="manual">Manual · Excel .xlsx / .xls</option>
                  </select>
                </Field>
              )}
              <Field
                label="Tên gói *"
                hint="Tên thư mục cho gói tự động; không dùng dấu cách hoặc ký tự đường dẫn."
              >
                <input name="ten" required maxLength={128} />
              </Field>
            </>
          )}
          <Field
            label={kind === 'private' ? 'Chọn file *' : 'File *'}
            hint={
              kind === 'package'
                ? 'ZIP tối đa 64 MB. Manual cần Excel; tự động cần .tc / .mtc.'
                : kind === 'private'
                  ? 'Tối đa 100 file; tổng dung lượng 256 MB mỗi lần.'
                  : 'Một file tối đa 256 MB.'
            }
          >
            <input
              name="file"
              type="file"
              required
              multiple={kind === 'private'}
              accept={kind === 'package' ? '.zip' : undefined}
            />
          </Field>
          {kind !== 'package' && (
            <Field label="Mô tả">
              <textarea name="moTa" maxLength={512} rows={3} />
            </Field>
          )}
        </fieldset>
        <ErrorMessage>{error}</ErrorMessage>
        {busy && (
          <div className="upload-progress" role="status">
            <progress max={100} value={progress} />
            <span>{progress === 100 ? 'Đang kiểm tra và lưu file…' : `Đã gửi ${progress}%`}</span>
          </div>
        )}
        <div className="form-footer">
          <Button onClick={() => (busy ? controller.current?.abort() : onClose())}>
            {busy ? 'Huỷ tải' : 'Huỷ'}
          </Button>
          <Button type="submit" variant="primary" busy={busy}>
            <Upload size={14} />
            Tải lên
          </Button>
        </div>
      </form>
    </Modal>
  )
}
