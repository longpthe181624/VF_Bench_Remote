// Tái hiện origin HTTP ngoài localhost: crypto.randomUUID không tồn tại như trên IP server.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright')
const assert = require('node:assert/strict')
const upstream = process.env.REQUEST_TEST_SERVER || 'http://127.0.0.1:5077'
const origin = 'http://bench-console.test'

;(async () => {
  const browser = await chromium.launch({
    headless: true,
    ...(process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {}),
  })
  try {
    const page = await browser.newPage()
    const errors = []
    page.on('pageerror', (error) => errors.push(error.message))
    // Giữ origin không an toàn trong browser; chuyển request đến server test, không dùng DNS/mạng thật.
    await page.route(origin + '/**', async (route) => {
      const request = route.request()
      const response = await page.request.fetch(request.url().replace(origin, upstream), {
        method: request.method(), headers: request.headers(), data: request.postDataBuffer(),
      })
      await route.fulfill({ response })
    })
    await page.goto(origin + '/app/')
    await page.getByLabel('Email', { exact: true }).fill('admin@benchconsole.local')
    await page.getByLabel('Mật khẩu', { exact: true }).fill('Admin@12345')
    await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click()
    await page.locator('.app-shell').waitFor()
    assert.deepEqual(await page.evaluate(() => ({ secure: isSecureContext, uuid: typeof crypto.randomUUID })),
      { secure: false, uuid: 'undefined' })

    await page.getByRole('link', { name: 'Request test', exact: true }).click()
    await page.getByRole('button', { name: 'Tạo Request', exact: true }).click()
    await page.getByLabel('Tên Request *', { exact: true }).fill('Request HTTP regression')
    await page.getByRole('button', { name: 'Lưu nháp', exact: true }).click()
    await page.getByRole('button', { name: 'Sửa nháp', exact: true }).waitFor()
    await page.reload()
    await page.getByRole('button', { name: 'Sửa nháp', exact: true }).click()
    assert.equal(await page.getByLabel('Tên Request *', { exact: true }).inputValue(), 'Request HTTP regression')
    await page.goBack()
    await page.getByRole('button', { name: 'Sửa nháp', exact: true }).waitFor()
    await page.getByRole('link', { name: 'Thiết bị', exact: true }).click()
    await page.getByRole('button', { name: 'Đăng ký thiết bị', exact: true }).waitFor()
    assert.deepEqual(errors, [])

    // Lỗi render khác ở một page phải giữ app-shell và cho Back/menu khôi phục.
    await page.evaluate(() => {
      window.savedGetRandomValues = crypto.getRandomValues.bind(crypto)
      crypto.getRandomValues = () => { throw new Error('Intentional request render failure') }
    })
    await page.getByRole('link', { name: 'Request test', exact: true }).click()
    await page.getByRole('button', { name: 'Tạo Request', exact: true }).click()
    await page.getByText('Không thể hiển thị trang này.', { exact: false }).waitFor()
    assert.equal(await page.locator('.app-shell').count(), 1)
    await page.evaluate(() => { crypto.getRandomValues = window.savedGetRandomValues })
    await page.goBack()
    await page.getByRole('button', { name: 'Tạo Request', exact: true }).waitFor()
    await page.getByRole('button', { name: 'Tạo Request', exact: true }).click()
    await page.getByLabel('Tên Request *', { exact: true }).waitFor()
    console.log('OK: HTTP Request create/edit/save, reload, Back, and render error recovery without blank app.')
  } finally { await browser.close() }
})().catch((error) => { console.error(error); process.exitCode = 1 })
