import { useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Plus } from 'lucide-react'
import { useApi } from '../hooks/use-api'
import { post, patch, errorText } from '../services/api'
import { date, matches } from '../lib/format'
import { notify } from '../lib/notify'
import { MutationForm } from '../components/MutationForm'
import { Badge, Button, DataTable, ErrorMessage, Field, Modal, Notice, PageHeading, Toolbar } from '../components/ui'

const stateName = { active: 'Hoạt động', revoked: 'Đã thu hồi', expired: 'Hết hạn' }
const permissionNames = {
  'DULIEU.VIEW': 'Xem và tải file dữ liệu chung',
  'DULIEU.UPLOAD': 'Upload file mới vào dữ liệu chung / File DBC',
  'CLIENT.CATALOG.CREATE': 'Tra cứu và thêm Model / Category / Type',
}

export default function ClientKeysPage() {
  const cache = useQueryClient(), keys = useApi('/client-api-keys'), permissions = useApi('/client-api-keys/permissions')
  const [q, setQ] = useState(''), [editing, setEditing] = useState(null), [revealed, setRevealed] = useState(null)
  const [error, setError] = useState(''), [busy, setBusy] = useState(false), keyInput = useRef(null)
  const invalidate = () => cache.invalidateQueries({ predicate: query => query.queryKey[1] === '/client-api-keys' })
  async function save(form) {
    const body = { permissions: form.getAll('permissions') }
    if (editing.id) {
      await patch(`/client-api-keys/${editing.id}/permissions`, { ...body, revision: editing.revision })
      notify('Đã cập nhật quyền. Quyền mới áp dụng ở lần gọi API tiếp theo.')
    } else {
      const expires = form.get('expiresAt')
      const created = await post('/client-api-keys', { ...body, name: form.get('name'), expiresAt: expires ? expires + '+07:00' : null })
      setRevealed(created)
    }
    await invalidate()
  }
  async function revoke(key) {
    if (!window.confirm(`Thu hồi key ${key.name}? Tool dùng key này sẽ không gọi API được nữa.`)) return
    setBusy(true); setError('')
    try { await post(`/client-api-keys/${key.id}/revoke`, { revision: key.revision }); await invalidate(); notify('Đã thu hồi API key.') }
    catch (e) { setError(errorText(e)) }
    finally { setBusy(false) }
  }
  return <>
    <PageHeading title="API key cho Client" description="Cấp quyền riêng cho từng tool, không cần đăng nhập tài khoản web." />
    <Notice>Key chỉ dùng cho các API file và danh mục đã cho phép. Không cấp quyền quản trị, ra lệnh bench, sửa / xoá file hay chuyển Release. Có thể đổi quyền hoặc thu hồi bất kỳ lúc nào.</Notice>
    <Toolbar value={q} onSearch={setQ} placeholder="Tìm tên tool / mã key" actions={<Button variant="primary" onClick={() => setEditing({})}><Plus size={14} />Cấp API key</Button>} />
    <ErrorMessage>{error || keys.error && errorText(keys.error) || permissions.error && errorText(permissions.error)}</ErrorMessage>
    <DataTable rows={(keys.data || []).filter(key => matches(key, q, ['name', 'keyId']))} loading={keys.isLoading} columns={[
      { key: 'name', label: 'Tool / Client', render: key => <><strong>{key.name}</strong><small>bck_{key.keyId} ·••••</small></> },
      { key: 'permissions', label: 'Quyền', render: key => <div className="flex flex-wrap gap-1">{key.permissions.map(p => <Badge key={p}>{permissionNames[p] || p}</Badge>)}</div> },
      { key: 'state', label: 'Trạng thái', render: key => <Badge tone={key.state === 'active' ? 'success' : 'neutral'}>{stateName[key.state]}</Badge> },
      { key: 'expiresAt', label: 'Hết hạn', render: key => key.expiresAt ? date(key.expiresAt) : 'Không đặt hạn' },
      { key: 'createdAt', label: 'Tạo lúc', render: key => date(key.createdAt) },
      { key: 'actions', label: 'Thao tác', render: key => key.state === 'active' && <div className="actions"><Button disabled={busy} onClick={() => setEditing(key)}>Đổi quyền</Button><Button disabled={busy} variant="danger" onClick={() => revoke(key)}>Thu hồi</Button></div> },
    ]} />
    <Modal open={!!editing} onClose={() => setEditing(null)} title={editing?.id ? `Quyền · ${editing.name}` : 'Cấp API key cho tool'}>
      {editing && <MutationForm onSave={save} onClose={() => setEditing(null)} label={editing.id ? 'Lưu quyền' : 'Cấp key'}>
        {!editing.id && <><Field label="Tên tool / Client *"><input name="name" required maxLength={128} placeholder="Ví dụ: Tool upload tại Lab 1" /></Field><Field label="Hết hạn (giờ Việt Nam)" hint="Để trống: key dùng được cho đến khi Admin thu hồi."><input name="expiresAt" type="datetime-local" /></Field></>}
        <div className="check-group"><span className="field-label">Quyền được cấp — chọn ít nhất một quyền</span><div>{(permissions.data || []).map(permission => <label className="check" key={permission.ma}><input name="permissions" type="checkbox" value={permission.ma} defaultChecked={editing.permissions?.includes(permission.ma)} /><span>{permissionNames[permission.ma] || permission.ten}<small>{permission.ma}</small></span></label>)}</div></div>
      </MutationForm>}
    </Modal>
    <Modal open={!!revealed} onClose={() => setRevealed(null)} title={`API key · ${revealed?.key.name || ''}`}>
      {revealed && <><Notice>Key chỉ hiển thị lần này. Sao chép để cấu hình tool; server không thể hiển thị lại key. Nếu mất key, thu hồi rồi cấp key mới.</Notice><Field label="API key"><textarea aria-label="API key" ref={keyInput} readOnly rows={3} value={revealed.apiKey} spellCheck={false} /></Field><p className="muted">Tool gửi header X-API-Key. Chọn key và nhấn Ctrl+C để sao chép.</p><div className="form-footer"><Button onClick={() => { keyInput.current?.focus(); keyInput.current?.select() }}>Chọn key</Button><Button variant="primary" onClick={() => setRevealed(null)}>Đã lưu, đóng</Button></div></>}
    </Modal>
  </>
}
