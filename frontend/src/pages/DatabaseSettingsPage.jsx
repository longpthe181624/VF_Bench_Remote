import { DATABASE_FIELDS } from '../lib/database-fields'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { Plus } from 'lucide-react'
import { useApi } from '../hooks/use-api'
import { patch, post, remove, errorText } from '../services/api'
import { matches } from '../lib/format'
import { notify } from '../lib/notify'
import {
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
export default function DatabaseSettingsPage() {
  const navigate = useNavigate()
  const cache = useQueryClient()
  const query = useApi('/database/lookups', 'DULIEU.VIEW')
  const [kind, setKind] = useState('models')
  const [editing, setEditing] = useState(null)
  const [q, setQ] = useState('')
  const [error, setError] = useState('')
  return (
    <>
      <PageHeading
        title="Danh mục File DBC"
        description="Admin quản lý tên Model / Category / Type. Mã và ID cố định."
      >
        <Button onClick={() => navigate('/files?category=dbc')}>← File DBC</Button>
      </PageHeading>
      <div className="tabs">
        {DATABASE_FIELDS.map(({ key, label }) => (
          <Button
            className={kind === key ? 'selected' : ''}
            variant="ghost"
            key={key}
            onClick={() => {
              setKind(key)
              setQ('')
              setError('')
            }}
          >
            {label}
          </Button>
        ))}
      </div>
      <Toolbar
        value={q}
        onSearch={setQ}
        placeholder="Tìm mã / tên"
        actions={
          <Button variant="primary" onClick={() => setEditing({})}>
            <Plus size={14} />
            Thêm {DATABASE_FIELDS.find((group) => group.key === kind).label}
          </Button>
        }
      />
      <ErrorMessage>{error || (query.error && errorText(query.error))}</ErrorMessage>
      <DataTable
        loading={query.isLoading}
        rows={(query.data?.[kind] || []).filter((r) => matches(r, q, ['ma', 'ten']))}
        columns={[
          { key: 'ma', label: 'Mã' },
          { key: 'ten', label: 'Tên' },
          {
            key: 'actions',
            label: 'Thao tác',
            render: (r) => (
              <div className="actions">
                <Button onClick={() => setEditing(r)}>Đổi tên</Button>
                <Button
                  variant="danger"
                  onClick={async () => {
                    if (!window.confirm(`Xoá ${r.ten}? Danh mục còn file sẽ không được xoá.`))
                      return
                    try {
                      await remove(`/database/lookups/${kind}/${r.id}`)
                      await cache.invalidateQueries()
                      notify('Đã xoá danh mục.')
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
      <Notice>
        ALL trong bộ lọc nghĩa là xem tất cả. Category “Áp dụng chung” (mã ALL) dùng cho file chung;
        Client lựa chọn rõ ràng, không tự ghi đè file của Category khác.
      </Notice>
      <Modal
        open={!!editing}
        title={editing?.id ? 'Đổi tên danh mục' : 'Thêm danh mục'}
        onClose={() => setEditing(null)}
      >
        {editing && (
          <MutationForm
            onClose={() => setEditing(null)}
            onSave={async (form) => {
              const body = Object.fromEntries(form)
              if (editing.id) await patch(`/database/lookups/${kind}/${editing.id}`, body)
              else await post(`/database/lookups/${kind}`, body)
              await cache.invalidateQueries()
              notify('Đã lưu danh mục.')
            }}
          >
            <Field label="Mã *">
              <input
                name="ma"
                required
                readOnly={!!editing.id}
                defaultValue={editing.ma || ''}
                maxLength={32}
                pattern="[A-Za-z0-9_-]+"
              />
            </Field>
            <Field label="Tên *">
              <input name="ten" required defaultValue={editing.ten || ''} maxLength={128} />
            </Field>
          </MutationForm>
        )}
      </Modal>
    </>
  )
}
