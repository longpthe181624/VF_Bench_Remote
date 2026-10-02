import { useState } from 'react'
import { Button, ErrorMessage } from './ui'
import { errorText } from '../services/api'
export function MutationForm({onSave,onClose,children,label='Lưu'}) {
  const [busy,setBusy]=useState(false),[error,setError]=useState('')
  async function submit(e){e.preventDefault();setBusy(true);setError('');try{await onSave(new FormData(e.currentTarget));onClose?.()}catch(e){setError(errorText(e))}finally{setBusy(false)}}
  return <form onSubmit={submit}><fieldset disabled={busy} className="form-fields">{children}</fieldset><ErrorMessage>{error}</ErrorMessage><div className="form-footer"><Button disabled={busy} onClick={onClose}>Huỷ</Button><Button type="submit" variant="primary" busy={busy}>{label}</Button></div></form>
}
