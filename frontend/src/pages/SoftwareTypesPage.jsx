import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { Plus, ArrowLeft } from 'lucide-react'
import { useApi } from '../hooks/use-api'
import { post, patch, remove, errorText } from '../services/api'
import { matches } from '../lib/format'
import { notify } from '../lib/notify'
import {
  Button,
  Field,
  ErrorMessage,
  Modal,
  PageHeading,
  Toolbar,
  DataTable,
} from '../components/ui'
import { MutationForm } from '../components/MutationForm'

export default function SoftwareTypesPage() {
  const navigate = useNavigate()
  const cache = useQueryClient()
  const query = useApi('/software/types', 'DULIEU.VIEW')
  const [q, setQ] = useState('')
  const [editing, setEditing] = useState(null)
  const [error, setError] = useState('')
  return (
    <>
      <PageHeading
        title="Type phần mềm"
        description="Admin thêm, đổi tên và xoá các loại phần mềm."
      >
        <Button onClick={() => navigate('/software')}>
          <ArrowLeft size={14} />
          Phần mềm
        </Button>
      </PageHeading>
      <Toolbar
        value={q}
        onSearch={setQ}
        placeholder="Tìm tên Type"
        actions={
          <Button variant="primary" onClick={() => setEditing({})}>
            <Plus size={14} />
            Thêm Type
          </Button>
        }
      />
      <ErrorMessage>{error || (query.error && errorText(query.error))}</ErrorMessage>
      <DataTable
        rows={(query.data || []).filter((r) => matches(r, q, ['ten']))}
        loading={query.isLoading}
        columns={[
          { key: 'ten', label: 'Tên Type' },
          { key: 'soFile', label: 'Số file' },
          {
            key: 'actions',
            label: 'Thao tác',
            render: (r) => (
              <div className="actions">
                <Button onClick={() => setEditing(r)}>Đổi tên</Button>
                <Button
                  variant="danger"
                  disabled={r.soFile > 0}
                  onClick={async () => {
                    if (!window.confirm(`Xoá Type ${r.ten}?`)) return
                    try {
                      await remove(`/software/types/${r.id}`)
                      await cache.invalidateQueries()
                      notify('Đã xoá Type.')
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
        title={editing?.id ? 'Đổi tên Type' : 'Thêm Type'}
      >
        {editing && (
          <MutationForm
            onClose={() => setEditing(null)}
            onSave={async (form) => {
              const body = { ten: form.get('ten') }
              if (editing.id) await patch(`/software/types/${editing.id}`, body)
              else await post('/software/types', body)
              await cache.invalidateQueries()
              notify('Đã lưu Type phần mềm.')
            }}
          >
            <Field label="Tên Type *">
              <input
                name="ten"
                required
                maxLength={128}
                defaultValue={editing.ten || ''}
                placeholder="Ứng dụng, Lib, Public hoặc tên khác"
              />
            </Field>
          </MutationForm>
        )}
      </Modal>
    </>
  )
}
