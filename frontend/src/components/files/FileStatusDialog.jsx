import { useQueryClient } from '@tanstack/react-query'
import { changeFileStatus } from '../../services/files'
import { notify } from '../../lib/notify'
import { DetailList, Field, Modal, Notice } from '../ui'
import { MutationForm } from '../MutationForm'

export function FileStatusDialog({ file, kind = 'shared', onClose }) {
  const cache = useQueryClient()
  const database = kind === 'database'

  async function save(form) {
    try {
      await changeFileStatus(kind, file, form.get('status'))
      notify(
        database ? 'Đã lưu trạng thái. Client sẽ nhận cập nhật.' : 'Đã lưu trạng thái phần mềm.',
      )
    } finally {
      await cache.invalidateQueries()
    }
  }

  return (
    <Modal open title={'Trạng thái · ' + (database ? file.tenFile : file.ten)} onClose={onClose}>
      <MutationForm label="Lưu trạng thái" onClose={onClose} onSave={save}>
        {database && (
          <DetailList
            items={[
              ['ID / phiên bản', `${file.id} · ${file.phienBan}`],
              ['Model / Category / Type', `${file.model} / ${file.category} / ${file.type}`],
              ['Hiện tại', file.status],
            ]}
          />
        )}
        <Field label={database ? 'Trạng thái mới' : 'Trạng thái'}>
          <select name="status" defaultValue={file.status}>
            <option value="Draft">Draft · Đang kiểm thử</option>
            <option value="Release">Release · Được phép sử dụng</option>
          </select>
        </Field>
        <Notice>
          {database
            ? 'Đổi trạng thái áp dụng cho bản file này. Lượt test đang chạy giữ nguyên bản đã chọn.'
            : 'Release khóa chỉnh sửa và xoá. Chuyển về Draft để cập nhật file.'}
        </Notice>
      </MutationForm>
    </Modal>
  )
}
