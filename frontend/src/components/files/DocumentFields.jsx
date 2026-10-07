import { useId } from 'react'
import { useApi } from '../../hooks/use-api'
import { documentFields } from '../../lib/document-fields'
import { Field } from '../ui'



export function DocumentFields({ file = {}, disabled = false }) {
  const id = useId()
  const lookups = useApi('/du-lieu-chung/document-lookups', 'DULIEU.VIEW')
  return <div className="form-grid">
    {documentFields.map(field => <Field key={field.key} label={field.label}>
      <input name={field.key} aria-label={field.label} defaultValue={file[field.key] || ''}
        readOnly={disabled} maxLength={128} placeholder={field.placeholder} list={`${id}-${field.key}`} />
      <datalist id={`${id}-${field.key}`}>
        {(lookups.data?.[field.key] || []).map(value => <option key={value} value={value} />)}
      </datalist>
    </Field>)}
  </div>
}
