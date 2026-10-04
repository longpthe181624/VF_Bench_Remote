import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { Plus } from 'lucide-react'
import { useApi } from '../hooks/use-api'
import { patch, post, remove, errorText } from '../services/api'
import { notify } from '../lib/notify'
import { matches } from '../lib/format'
import {
  Button,
  DataTable,
  ErrorMessage,
  Field,
  Modal,
  PageHeading,
  Toolbar,
} from '../components/ui'
import { MutationForm } from '../components/MutationForm'
export default function CategoriesPage() {
  const cache = useQueryClient()
  const navigate = useNavigate()
  const categories = useApi('/du-lieu-chung/muc', 'DULIEU.VIEW')
  const [editing, setEditing] = useState(null)
  const [q, setQ] = useState('')
  const [error, setError] = useState('')
  return (
    <>
      <PageHeading
        title="Mục dữ liệu chung"
        description="Thêm mục lưu file; không thể xoá mục mặc định hoặc mục còn file."
      >
        <Button onClick={() => navigate('/files')}>← Dữ liệu chung</Button>
      </PageHeading>
      <Toolbar
        value={q}
        onSearch={setQ}
        actions={
          <Button variant="primary" onClick={() => setEditing({})}>
            <Plus size={14} />
            Thêm mục
          </Button>
        }
      />
      <ErrorMessage>{error || (categories.error && errorText(categories.error))}</ErrorMessage>
      <DataTable
        rows={(categories.data || []).filter((c) => matches(c, q, ['ma', 'ten', 'moTa']))}
        columns={[
          { key: 'ten', label: 'Tên mục' },
          { key: 'moTa', label: 'Mô tả' },
          { key: 'soFile', label: 'Số file' },
          {
            key: 'actions',
            label: 'Thao tác',
            render: (c) => (
              <div className="actions">
                <Button onClick={() => setEditing(c)}>Sửa</Button>
                <Button
                  variant="danger"
                  disabled={c.macDinh || c.soFile > 0}
                  onClick={async () => {
                    if (!window.confirm(`Xoá mục ${c.ten}?`)) return
                    try {
                      await remove(`/du-lieu-chung/muc/${encodeURIComponent(c.ma)}`)
                      await cache.invalidateQueries()
                      notify('Đã xoá mục.')
                    } catch (e) {
                      setError(errorText(e))
                    }
                  }}
                >
                  Xoá
                </Button>
              </div>
            ),
          },
        ]}
      />
      <Modal
        open={!!editing}
        onClose={() => setEditing(null)}
        title={editing?.ma ? 'Sửa mục' : 'Thêm mục'}
      >
        {editing && (
          <MutationForm
            onClose={() => setEditing(null)}
            onSave={async (form) => {
              const body = Object.fromEntries(form)
              if (editing.ma)
                await patch(`/du-lieu-chung/muc/${encodeURIComponent(editing.ma)}`, body)
              else await post('/du-lieu-chung/muc', body)
              await cache.invalidateQueries()
              notify('Đã lưu mục.')
            }}
          >
            <Field label="Tên mục *">
              <input name="ten" required defaultValue={editing.ten || ''} maxLength={128} />
            </Field>
            <Field label="Mô tả">
              <textarea name="moTa" defaultValue={editing.moTa || ''} rows={3} />
            </Field>
          </MutationForm>
        )}
      </Modal>
    </>
  )
}
