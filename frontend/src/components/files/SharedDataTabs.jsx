import { useNavigate } from 'react-router-dom'
import { useSharedDataSections } from '../../hooks/use-shared-data-sections'
import { Button } from '../ui'

export function SharedDataTabs() {
  const navigate = useNavigate()
  const { sections } = useSharedDataSections()
  return (
    <div className="tabs">
      {sections.map((section) => (
        <Button
          key={section.path}
          variant="ghost"
          className={section.active ? 'selected' : ''}
          onClick={() => navigate(section.path)}
        >
          {section.label}
          {section.count !== undefined && <span className="tab-count">{section.count}</span>}
        </Button>
      ))}
    </div>
  )
}
