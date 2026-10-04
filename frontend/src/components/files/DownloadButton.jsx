import { useState } from 'react'
import { Download } from 'lucide-react'
import { Button } from '../ui'
import { download, errorText } from '../../services/api'
import { notify } from '../../lib/notify'
import { fileDownloadPath, fileName } from '../../services/files'
export function DownloadButton({ kind, file }) {
  const [busy, setBusy] = useState(false)
  return (
    <Button
      busy={busy}
      onClick={async () => {
        setBusy(true)
        try {
          await download(fileDownloadPath(kind, file), fileName(kind, file))
        } catch (e) {
          notify(errorText(e))
        } finally {
          setBusy(false)
        }
      }}
    >
      <Download size={13} />
      Tải về
    </Button>
  )
}
