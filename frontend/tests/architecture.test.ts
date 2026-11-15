// @vitest-environment node
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { dirname, join, relative, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * Import boundaries, enforced like the backend's layer tests:
 *
 *   app/ ──▶ pages/ ──▶ features/<name>/ ──▶ shared/
 *
 * - shared/ is the foundation (API client, UI kit, i18n, utils) and imports nothing above it.
 * - A feature reaches another feature only through its public index.ts, never its internals.
 * - Only app/ wires pages; nothing below app/ imports pages/ or app/.
 * - Relative imports stay inside their own folder; anything further uses the '@/...' alias.
 */
const SRC = resolve(dirname(fileURLToPath(import.meta.url)), '../src')

function sourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((name: string) => {
    const path = join(dir, name)
    if (statSync(path).isDirectory()) return sourceFiles(path)
    return /\.(ts|tsx)$/.test(name) && !/\.test\.tsx?$/.test(name) ? [path] : []
  })
}

interface Import {
  file: string
  spec: string
  target: string // path relative to src, e.g. features/market/index
}

function importsOf(file: string): Import[] {
  const text = readFileSync(file, 'utf8')
  const specs = [...text.matchAll(/(?:import|export)\s[^'"]*?from\s+['"]([^'"]+)['"]|import\(\s*['"]([^'"]+)['"]\s*\)/g)].map((m) => m[1] ?? m[2]!)
  return specs
    .filter((spec) => spec.startsWith('@/') || spec.startsWith('.'))
    .map((spec) => ({
      file: relative(SRC, file).split(sep).join('/'),
      spec,
      target: (spec.startsWith('@/') ? spec.slice(2) : relative(SRC, resolve(dirname(file), spec)).split(sep).join('/')),
    }))
}

const area = (path: string) => path.split('/')[0]! // app | pages | features | shared | main.tsx
const featureOf = (path: string) => (path.startsWith('features/') ? path.split('/')[1] : undefined)

const all = sourceFiles(SRC).flatMap(importsOf)

describe('frontend architecture', () => {
  it('found the source tree', () => {
    expect(all.length).toBeGreaterThan(50)
  })

  it('shared/ does not depend on features, pages or the app shell', () => {
    const violations = all.filter((i) => area(i.file) === 'shared' && ['features', 'pages', 'app'].includes(area(i.target)))
    expect(violations).toEqual([])
  })

  it('features use each other only through their public index', () => {
    const violations = all.filter((i) => {
      const from = featureOf(i.file)
      const to = featureOf(i.target)
      if (!to || from === to) return false
      return i.target !== `features/${to}` && i.target !== `features/${to}/index`
    })
    expect(violations).toEqual([])
  })

  it('pages and the app shell also go through feature indexes', () => {
    const violations = all.filter(
      (i) => ['pages', 'app'].includes(area(i.file)) && featureOf(i.target) && i.target.split('/').length > 2 && !i.target.endsWith('/index'),
    )
    expect(violations).toEqual([])
  })

  it('only the app shell imports pages, and nothing imports the app shell', () => {
    const violations = all.filter(
      (i) => (area(i.target) === 'pages' && area(i.file) !== 'app') || (area(i.target) === 'app' && !['app', 'main.tsx'].includes(area(i.file))),
    )
    expect(violations).toEqual([])
  })

  it('relative imports never leave their own folder', () => {
    const violations = all.filter((i) => i.spec.startsWith('../'))
    expect(violations).toEqual([])
  })
})
