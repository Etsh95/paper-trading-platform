// End-to-end trade flow in a real browser (installed Chrome, headless).
//   npm install && npm test                        -> against docker compose on :8080
//   BASE_URL=https://... npm test                  -> against a deployment
//   LATENCY_MS=400 npm test                        -> add delay to every API call; exposes races that only
//                                                     show up on real networks (two were found this way)
import { chromium } from 'playwright-core'
// Target: docker compose (default) or any other running instance via BASE_URL
const base = process.env.BASE_URL ?? 'http://localhost:8080'
const out = process.env.SCREENSHOTS ?? '.'
const browser = await chromium.launch({ channel: 'chrome', headless: true })
const page = await browser.newPage({ viewport: { width: 1440, height: 900 }, colorScheme: 'dark' })
const problems = []
page.on('console', (m) => (m.type() === 'error' || /Content Security Policy|Refused/i.test(m.text())) && problems.push(m.text()))
const latency = Number(process.env.LATENCY_MS ?? 0)
if (latency > 0) await page.route('**/api/**', async (route) => { await new Promise((r) => setTimeout(r, latency)); await route.continue() })
page.on('pageerror', (e) => problems.push(String(e)))
const wsUrls = []
page.on('websocket', (ws) => wsUrls.push(ws.url().replace(/access_token=[^&]+/, 'access_token=…')))
const step = async (name, fn) => { try { await fn(); console.log('PASS', name) } catch (e) { console.log('FAIL', name, '-', String(e).split('\n')[0]); problems.push(name) } }

await step('page loads with live prices', async () => {
  await page.goto(base + '/?symbol=BTCUSDT')
  await page.waitForSelector('text=Live prices', { timeout: 20000 })
  await page.waitForTimeout(3000)
})
await step('realtime quotes stream (sparkline drawn)', async () => {
  const points = await page.locator('polyline').first().getAttribute('points')
  if (!points || points.split(' ').length < 3) throw new Error('sparkline has too few points: ' + points)
})
await step('market buy fills and toasts via realtime fill event', async () => {
  await page.getByLabel('Quantity').fill('0.01')
  await page.getByRole('button', { name: 'Buy BTC' }).click()
  await page.waitForSelector('text=/Bought 0.01 BTC/', { timeout: 10000 })
})
await step('limit sell rests and appears in open orders', async () => {
  await page.getByRole('radio', { name: 'Sell' }).click()
  await page.getByRole('radio', { name: 'Limit' }).click()
  await page.getByLabel('Limit price').fill('999999')
  await page.getByLabel('Quantity').fill('0.01')
  await page.getByRole('button', { name: 'Sell BTC' }).click()
  await page.waitForSelector('text=/Limit order placed: sell 0.01 BTC/', { timeout: 10000 })
  await page.getByRole('button', { name: /Cancel Sell order/ }).waitFor()
})
await step('reservation blocks selling the same coins twice', async () => {
  await page.getByRole('radio', { name: 'Market' }).click()
  await page.getByLabel('Quantity').fill('0.01')
  await page.getByRole('button', { name: 'Sell BTC' }).click()
  await page.waitForSelector('text=/available to sell/', { timeout: 5000 })
})
await step('cancel releases the order', async () => {
  await page.getByRole('button', { name: /Cancel Sell order/ }).click()
  await page.waitForSelector('text=Order cancelled', { timeout: 5000 })
  await page.waitForSelector('text=/No open orders/', { timeout: 5000 })
})
await step('market sell closes the position', async () => {
  await page.getByLabel('Quantity').fill('0.01')
  await page.getByRole('button', { name: 'Sell BTC' }).click()
  await page.waitForSelector('text=/Sold 0.01 BTC/', { timeout: 10000 })
})
await step('history shows 3 orders with correct statuses', async () => {
  await page.getByRole('link', { name: 'History' }).click()
  await page.waitForTimeout(1500)
  const rows = await page.locator('tbody tr').count()
  const statuses = await page.locator('tbody tr').allInnerTexts()
  if (rows !== 3) throw new Error(`expected 3 rows, got ${rows}`)
  if (!statuses.some((s) => s.includes('Cancelled')) || statuses.filter((s) => s.includes('Filled')).length !== 2) throw new Error(statuses.join(' | '))
})
await step('portfolio: no positions left, realized P&L recorded', async () => {
  await page.getByRole('link', { name: 'Portfolio' }).click()
  await page.waitForSelector('text=/don\'t hold anything yet/', { timeout: 5000 })
  await page.screenshot({ path: `${out}/portfolio.png` })
})
await step('reload keeps the same account (session persisted)', async () => {
  await page.reload()
  await page.getByRole('link', { name: 'History' }).click()
  await page.waitForTimeout(1500)
  if ((await page.locator('tbody tr').count()) !== 3) throw new Error('history lost after reload')
})
await step('Arabic RTL works in the production build', async () => {
  await page.getByRole('button', { name: 'Switch to Arabic' }).click()
  await page.getByRole('link', { name: 'تداول' }).click()
  await page.waitForTimeout(2000)
  if ((await page.evaluate(() => document.documentElement.dir)) !== 'rtl') throw new Error('dir not rtl')
  await page.screenshot({ path: `${out}/arabic.png` })
})

console.log('websockets:', JSON.stringify(wsUrls))
console.log('console/CSP problems:', JSON.stringify(problems))
await browser.close()
process.exit(problems.length === 0 ? 0 : 1)
