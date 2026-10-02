import { cp, mkdir, readFile, readdir, unlink } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const root = fileURLToPath(new URL('../', import.meta.url))
const source = path.join(root, 'frontend/dist')
const target = path.join(root, 'backend/BenchConsole.Api/wwwroot/app')
await readFile(path.join(source, 'index.html'))
await mkdir(target, { recursive: true })
await cp(source, target, { recursive: true })
// Remove only stale generated assets after the new index has been copied.
const current = new Set(await readdir(path.join(source, 'assets')))
for (const name of await readdir(path.join(target, 'assets'))) {
  if (!current.has(name)) await unlink(path.join(target, 'assets', name))
}
console.log('React FE đã được chép vào backend/BenchConsole.Api/wwwroot/app')
