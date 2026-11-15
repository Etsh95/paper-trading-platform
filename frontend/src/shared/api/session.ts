import { ApiError, toError } from './errors'
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
// Why the next session is being created. Kept until creation succeeds, so a failed "start over" or reset
// isn't later mistaken for the first-ever session.
let pendingReason: Exclude<SessionChangeReason, 'initial'> | null = null
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
  let response: Response
  try {
    response = await fetch(apiUrl('/api/sessions'), { method: 'POST' })
  } catch {
    throw new ApiError(0, 'network', 'Network error')
  }
  // Carries the server's code (e.g. capacity_reached when the demo is full) so callers can explain it.
  if (!response.ok) throw await toError(response)
  const session = (await response.json()) as Session
  current = session
  pendingReason = null
  save(session)
  listeners.forEach((l) => l(session, reason))
  return session
}

/** Returns the current session, creating an anonymous paper account on first use. */
export function ensureSession(): Promise<Session> {
  if (current) return Promise.resolve(current)
  pending ??= create(pendingReason ?? 'initial').finally(() => (pending = null))
  return pending
}

/**
 * Replaces the session. Used when the server no longer accepts our token (it keeps state in memory,
 * so a restart resets accounts) or when the user asks to start over.
 */
export function renewSession(reason: Exclude<SessionChangeReason, 'initial'> = 'reset'): Promise<Session> {
  current = null
  pendingReason = reason
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
