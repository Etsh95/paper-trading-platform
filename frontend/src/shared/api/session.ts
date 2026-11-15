import type { Session } from './types'
import { apiUrl } from './url'

const STORAGE_KEY = 'paperdesk.session'

/**
 * Why the session changed:
 * - `initial`: first session in this browser; nothing to reset.
 * - `created`: the user started over with a new account.
 * - `reset`: the server no longer accepts our token (in-memory state lost on restart).
 */
export type SessionChangeReason = 'initial' | 'created' | 'reset'

type Listener = (session: Session, reason: SessionChangeReason) => void

let current: Session | null = load()
let pending: Promise<Session> | null = null
const listeners = new Set<Listener>()

function load(): Session | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    const session = JSON.parse(raw) as Session
    return new Date(session.expiresAt).getTime() > Date.now() + 60_000 ? session : null
  } catch {
    return null
  }
}

function save(session: Session) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(session))
  } catch {
    // Private mode / blocked storage: the session simply won't survive a reload.
  }
}

async function create(reason: SessionChangeReason): Promise<Session> {
  const response = await fetch(apiUrl('/api/sessions'), { method: 'POST' })
  if (!response.ok) throw new Error(`Could not start a session (${response.status})`)
  const session = (await response.json()) as Session
  current = session
  save(session)
  listeners.forEach((l) => l(session, reason))
  return session
}

/** Returns the current session, creating an anonymous paper account on first use. */
export function ensureSession(): Promise<Session> {
  if (current) return Promise.resolve(current)
  pending ??= create('initial').finally(() => (pending = null))
  return pending
}

/**
 * Replaces the session. Used when the server no longer accepts our token (it keeps state in memory,
 * so a restart resets accounts) or when the user asks to start over.
 */
export function renewSession(reason: Exclude<SessionChangeReason, 'initial'> = 'reset'): Promise<Session> {
  current = null
  pending ??= create(reason).finally(() => (pending = null))
  return pending
}

export function currentToken(): string | null {
  return current?.accessToken ?? null
}

export function onSessionChange(listener: Listener): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}
