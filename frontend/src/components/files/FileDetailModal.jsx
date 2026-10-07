import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../../context/auth-context'
import { api, patch, errorText } from '../../services/api'
import { fileDownloadPath, updateDraftFile } from '../../services/files'
import { notify } from '../../lib/notify'
import { bytes, date } from '../../lib/format'
import { Button, DetailList, ErrorMessage, Field, Modal } from '../ui'
import { MutationForm } from '../MutationForm'
import { DocumentFields } from './DocumentFields'
import { SoftwareTypeSelect } from './SoftwareTypeSelect'
import { DownloadButton } from './DownloadButton'
export function FileDetailModal({ file, kind, categories = [], onClose, onStatus }) {
  const { can } = useAuth()
  const cache = useQueryClient()
  const [editCategory, setEditCategory] = useState(file.loai)
  const editable =
    kind === 'private'
      ? can('KHO.UPLOAD')
      : kind === 'shared' && can('DULIEU.UPLOAD') && file.status !== 'Release'
  const [preview, setPreview] = useState(null)
  const [previewBusy, setPreviewBusy] = useState(false)
  const [error, setError] = useState('')
  const controller = useRef(null)
  const blobUrl = useRef(null)
  useEffect(
    () => () => {
      controller.current?.abort()
      if (blobUrl.current) URL.revokeObjectURL(blobUrl.current)
    },
    [],
  )
  async function showPreview() {
    setError('')
    setPreviewBusy(true)
    try {
      const name = file.tenFile || file.tenFileGoc
      const ext = name.split('.').pop().toLowerCase()
      const images = {
        png: 'image/png',
        jpg: 'image/jpeg',
        jpeg: 'image/jpeg',
        gif: 'image/gif',
        webp: 'image/webp',
        bmp: 'image/bmp',
      }
      if (
        !images[ext] &&
        !['txt', 'log', 'csv', 'dbc', 'json', 'xml', 'md', 'yaml', 'yml', 'ini', 'cfg'].includes(
          ext,
        )
      )
        throw Error('Tải xuống để mở định dạng này bằng ứng dụng phù hợp.')
      if (file.kichThuoc > 2 * 1024 ** 2)
        throw Error('Xem trước hỗ trợ file tối đa 2 MB. Hãy tải file lớn xuống.')
      controller.current = new AbortController()
      const { data } = await api.get(fileDownloadPath(kind, file), {
        responseType: 'blob',
        signal: controller.current.signal,
      })
      if (data.size > 2 * 1024 ** 2) throw Error('File quá lớn để xem trước.')
      if (images[ext]) {
        if (blobUrl.current) URL.revokeObjectURL(blobUrl.current)
        blobUrl.current = URL.createObjectURL(new Blob([data], { type: images[ext] }))
        setPreview({ image: blobUrl.current })
      } else setPreview({ text: await data.text() })
    } catch (e) {
      setError(errorText(e))
    } finally {
      setPreviewBusy(false)
    }
  }
  async function save(form) {
    try {
      if (kind === 'shared') {
        form.set('softwareTypeId', Number(form.get('softwareTypeId')) || 0)
        await updateDraftFile('shared', file, form)
      } else
        await patch(`/storage/files/${file.id}`, {
          tenFile: form.get('tenFile'),
          moTa: form.get('moTa'),
        })
      notify('Đã cập nhật file.')
    } finally {
      await cache.invalidateQueries()
    }
  }
  const fields = (
    <>
      <div className="form-grid">
        <Field label={kind === 'shared' ? 'Tên hiển thị' : 'Tên file'}>
          <input
            name={kind === 'shared' ? 'ten' : 'tenFile'}
            defaultValue={kind === 'shared' ? file.ten : file.tenFile}
            maxLength={kind === 'shared' ? 128 : 260}
            required
            readOnly={!editable}
          />
        </Field>
        {kind === 'shared' && (
          <Field label="Mục lưu trữ">
            <select
              name="loai"
              value={editCategory}
              onChange={(e) => setEditCategory(e.target.value)}
              disabled={!editable}
            >
              {categories.map((c) => (
                <option key={c.ma} value={c.ma}>
                  {c.ten}
                </option>
              ))}
            </select>
          </Field>
        )}
      </div>
      {kind === 'shared' && editable && (
        <Field label="Thay file" hint="Để trống để giữ nội dung hiện tại.">
          <input type="file" name="file" />
        </Field>
      )}
      {kind === 'shared' && editCategory === 'phien-ban' && (
        <SoftwareTypeSelect
          defaultValue={file.softwareTypeId}
          disabled={!editable}
          required={editable}
        />
      )}
      {kind === 'shared' && editCategory === 'tai-lieu' && <DocumentFields file={file} disabled={!editable} />}
      <Field label="Mô tả">
        <textarea
          name="moTa"
          defaultValue={file.moTa || ''}
          readOnly={!editable}
          maxLength={512}
          rows={3}
        />
      </Field>
    </>
  )
  return (
    <Modal open onClose={onClose} title={file.ten || file.tenFile} wide>
      <DetailList
        items={[
          ['File gốc', file.tenFile || file.tenFileGoc],
          ...(kind === 'shared' && file.loai === 'phien-ban' ? [['Status', file.status]] : []),
          ['Dung lượng', bytes(file.kichThuoc)],
          ['Người tải lên', file.nguoiTaiLen || file.nguoiDung || file.benchCode],
          ['Thời gian', date(file.taiLenLuc || file.nhanLuc)],
          [
            'SHA-256',
            <code className="break-all" key="sha">
              {file.sha256}
            </code>,
          ],
        ]}
      />
      <div className="flex gap-2 my-5">
        <Button busy={previewBusy} onClick={showPreview}>
          Xem trước
        </Button>
        <DownloadButton file={file} kind={kind} />
        {kind === 'shared' && file.loai === 'phien-ban' && can('DATABASE.RELEASE') && (
          <Button onClick={() => onStatus?.(file)}>Đổi trạng thái</Button>
        )}
      </div>
      <ErrorMessage>{error}</ErrorMessage>
      {preview?.image && (
        <img
          className="file-preview-image"
          alt={file.tenFile || file.tenFileGoc}
          src={preview.image}
        />
      )}{' '}
      {preview?.text !== undefined && <pre className="json-preview">{preview.text}</pre>}
      {editable ? (
        <MutationForm onSave={save} onClose={onClose}>
          {fields}
        </MutationForm>
      ) : (
        <>
          {kind !== 'package' && kind !== 'report' && fields}
          <div className="form-footer">
            <Button onClick={onClose}>Đóng</Button>
          </div>
        </>
      )}
    </Modal>
  )
}
