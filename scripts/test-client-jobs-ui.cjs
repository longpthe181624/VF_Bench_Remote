// Run only against the temporary --serve-ui server (never the real deployment).
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright')
const assert = require('node:assert/strict')
const crypto = require('node:crypto')
const base = 'http://127.0.0.1:5077/app/'
const api = path => new URL('/api' + path, base).href
;(async () => {
  const browser = await chromium.launch({ headless: true, ...(process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {}) })
  try {
    const page = await browser.newPage(), errors = []
    page.on('pageerror', e => errors.push(e.message))
    await page.goto(base)
    await page.getByLabel('Email', { exact: true }).fill('admin@benchconsole.local')
    await page.getByLabel('Mật khẩu', { exact: true }).fill('Admin@12345')
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click()
    await page.getByRole('link', { name: 'API key cho Client', exact: true }).click()
    await page.getByRole('button', { name: 'Cấp API key', exact: true }).click()
    let dialog = page.getByRole('dialog')
    await dialog.getByLabel('Tên tool / Client *', { exact: true }).fill('Simulation job tool')
    await dialog.getByLabel('Nhận việc, tải kịch bản', { exact: false }).check()
    await dialog.getByLabel('Simulation bench', { exact: false }).check()
    const issued = page.waitForResponse(r => r.url() === api('/client-api-keys') && r.request().method() === 'POST')
    await dialog.getByRole('button', { name: 'Cấp key', exact: true }).click()
    const key = (await (await issued).json()).apiKey
    await page.getByRole('button', { name: 'Đã lưu, đóng', exact: true }).click()
    await page.goto(base + 'requests/new?device=SIMULATION-UI')
    await page.getByLabel('Tên Request *', { exact: true }).fill('SIMULATION Job UI')
    await page.getByRole('button', { name: 'Tiếp tục', exact: true }).click()
    await page.getByRole('button', { name: 'Tiếp tục', exact: true }).click()
    await page.getByLabel('Simulation-UI', { exact: false }).check()
    await page.getByRole('button', { name: 'Tiếp tục', exact: true }).click()
    await page.getByRole('button', { name: 'Tiếp tục', exact: true }).click()
    const enqueued = page.waitForResponse(r => r.url().endsWith('/enqueue') && r.request().method() === 'POST')
    await page.getByRole('button', { name: 'Gửi yêu cầu cho tool', exact: true }).click()
    const job = await (await enqueued).json()
    assert.equal(job.state, 'queued')
    await page.getByRole('button', { name: 'Huỷ việc chờ', exact: true }).waitFor()
    await page.reload()
    await page.getByText(job.code, { exact: true }).waitFor()
    const headers = { 'X-API-Key': key }, leaseId = crypto.randomUUID(), path = `/client/jobs/${job.code}`
    async function post(suffix, data) {
      const response = await page.request.post(api(path + suffix), { headers, data })
      assert.equal(response.status(), 200, 'Client call ' + suffix)
      return response.json()
    }
    await post('/claim', { leaseId })
    const verifiedFiles = []
    for (const file of job.files) {
      const response = await page.request.get(new URL(file.downloadUrl, base).href, { headers })
      assert.equal(response.status(), 200)
      const sha256 = crypto.createHash('sha256').update(await response.body()).digest('hex')
      assert.equal(sha256.toLowerCase(), file.sha256.toLowerCase())
      verifiedFiles.push({ fileId: file.id, sha256 })
    }
    await post('/progress', { leaseId, running: true, progress: 20, verifiedFiles, message: 'SIMULATION only' })
    const batch = { leaseId, batchId: crypto.randomUUID(), results: [{ fileId: job.files[0].id, caseId: 'SIM-1', name: 'SIMULATED testcase', verdict: 'pass' }] }
    await post('/results', batch)
    assert.equal((await post('/results', batch)).resultCount, 1)
    await post('/complete', { leaseId, state: 'completed', expectedResults: 1 })
    await page.getByText('SIMULATED testcase', { exact: true }).waitFor({ timeout: 15000 })
    await page.getByText('Hoàn tất', { exact: true }).first().waitFor()
    assert.equal(await page.getByRole('button', { name: 'Sửa nháp', exact: true }).count(), 0)
    assert.equal(await page.evaluate(secret => JSON.stringify(localStorage).includes(secret), key), false)
    assert.deepEqual(errors, [])
    console.log('OK: production FE tạo request -> key gán bench -> claim -> tải/verify SHA -> progress -> retry kết quả -> hoàn tất -> web hiển thị kết quả.')
  } finally { await browser.close() }
})().catch(e => { console.error(e); process.exitCode = 1 })
