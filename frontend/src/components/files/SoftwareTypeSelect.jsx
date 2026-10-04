import { useApi } from '../../hooks/use-api'
import { errorText } from '../../services/api'
import { ErrorMessage } from '../ui'
import { ClassificationSelect } from '../ClassificationSelect'
export function SoftwareTypeSelect({ defaultValue = '', disabled = false, required = false }) {
  const query = useApi('/software/types', 'DULIEU.VIEW')
  return (
    <>
      <ClassificationSelect
        label="Type phần mềm"
        name="softwareTypeId"
        items={query.data || []}
        defaultValue={defaultValue}
        disabled={disabled}
        required={required}
        loading={query.isLoading}
        path="/software/types"
      />
      <ErrorMessage>{query.error && errorText(query.error)}</ErrorMessage>
    </>
  )
}
