/**
 * Thin REST client for the surfaces that are deliberately not GraphQL:
 * cookie auth, the pre-auth setup wizard, health, and binary downloads.
 */

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    /** Per-field validation errors from an ASP.NET ValidationProblem, if any. */
    readonly errors?: Record<string, string[]>,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

interface ProblemDetails {
  title?: string
  detail?: string
  status?: number
  errors?: Record<string, string[]>
}

async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails | undefined

  try {
    problem = (await response.json()) as ProblemDetails
  } catch {
    // Not every failure is problem+json (proxies, 502s). Fall through to the status text.
  }

  const firstFieldError = problem?.errors
    ? Object.values(problem.errors).flat().find(Boolean)
    : undefined

  const message =
    problem?.detail ?? firstFieldError ?? problem?.title ?? response.statusText ?? 'Request failed.'

  return new ApiError(message, response.status, problem?.errors)
}

export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const response = await fetch(path, {
    // Auth is cookie-based, so every call has to carry them — including across the dev proxy.
    credentials: 'include',
    ...init,
    headers: {
      Accept: 'application/json',
      ...(init.body ? { 'Content-Type': 'application/json' } : {}),
      ...init.headers,
    },
  })

  if (!response.ok) throw await toApiError(response)
  return response
}

export async function apiJson<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await apiFetch(path, init)
  if (response.status === 204) return undefined as T

  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export function apiPost<T>(path: string, body?: unknown, init: RequestInit = {}): Promise<T> {
  return apiJson<T>(path, {
    method: 'POST',
    ...init,
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  })
}
