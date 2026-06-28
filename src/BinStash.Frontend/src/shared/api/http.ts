import { useSetupStore } from '@/features/setup/store/setup.store'
import { ApiError, isProblemJson, parseProblemDetail } from '@/shared/api/problemDetails'

export { ApiError } from '@/shared/api/problemDetails'
export type { ProblemDetail } from '@/shared/api/problemDetails'
export { throwForStatus } from '@/shared/api/problemDetails'

/**
 * Thin fetch wrapper that attaches credentials to every request.
 * Returns the raw Response — callers are responsible for status checking.
 */
export async function apiFetch(input: string, init?: RequestInit): Promise<Response> {
  return fetch(input, {
    ...init,
    credentials: 'include',
  })
}

/**
 * Fetch wrapper that:
 *  1. Attaches credentials.
 *  2. Detects the 503 "setup_required" plain-text sentinel and updates the
 *     setup store, then throws so upstream callers abort cleanly.
 *  3. Parses application/problem+json responses (RFC 7807) and throws an
 *     ApiError that carries the full structured ProblemDetail object.
 *  4. Falls back to a generic Error for any other non-2xx response.
 *  5. Returns the parsed JSON body on success.
 */
export async function apiJson<T>(input: string, init?: RequestInit): Promise<T> {
  const res = await apiFetch(input, init)

  // ── 503 setup sentinel ────────────────────────────────────────────────────
  // The backend emits a plain-text "setup_required" body (not JSON) before the
  // application has been initialised. Handle it before attempting JSON parsing.
  if (res.status === 503) {
    let text = ''
    try {
      text = await res.text()
    } catch { /* ignore read errors */ }

    if (text.trim() === 'setup_required') {
      const setupStore = useSetupStore()
      setupStore.status = null
      setupStore.error = 'setup_required'
      throw new Error('setup_required')
    }

    // Not a setup sentinel — fall through to normal error handling below, but
    // the body has been consumed so we can only build a generic error.
    throw new ApiError({ title: 'Service unavailable', status: 503 }, 503)
  }

  // ── Non-2xx responses ─────────────────────────────────────────────────────
  if (!res.ok) {
    // RFC 7807 / RFC 9457 structured error
    if (isProblemJson(res)) {
      throw await parseProblemDetail(res)
    }

    // Generic JSON body with a message/detail field
    let msg = `Request failed (${res.status})`
    try {
      const data = await res.json()
      msg = data?.detail ?? data?.message ?? msg
    } catch {
      try {
        const text = await res.text()
        if (text) msg = text
      } catch { /* ignore */ }
    }
    throw new ApiError({ title: msg, status: res.status }, res.status)
  }

  // ── Success ───────────────────────────────────────────────────────────────
  // 204 No Content (and any other empty-bodied success, e.g. DELETE endpoints)
  // carries no JSON to parse — calling res.json() on an empty body throws a
  // SyntaxError. Return undefined for these instead.
  if (res.status === 204 || res.headers.get('content-length') === '0') {
    return undefined as T
  }
  return (await res.json()) as T
}