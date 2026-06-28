/**
 * RFC 7807 / RFC 9457 Problem Details for HTTP APIs
 * Content-Type: application/problem+json
 */
export interface ProblemDetail {
  /** A URI reference that identifies the problem type. */
  type?: string
  /** A short, human-readable summary of the problem type. */
  title?: string
  /** The HTTP status code. */
  status?: number
  /** A human-readable explanation specific to this occurrence of the problem. */
  detail?: string
  /** A URI reference that identifies the specific occurrence of the problem. */
  instance?: string
  /** ASP.NET Core validation errors extension field. */
  errors?: Record<string, string[]>
  /** Allow any additional extension members. */
  [key: string]: unknown
}

/**
 * Error thrown when the server returns an application/problem+json response
 * or any non-2xx response. Carries the full structured ProblemDetail object.
 */
export class ApiError extends Error {
  /** The parsed ProblemDetail object (or a synthetic one for non-problem responses). */
  readonly problem: ProblemDetail
  /** HTTP status code of the response. */
  readonly status: number

  constructor(problem: ProblemDetail, status: number) {
    // Prefer detail → title → generic fallback as the Error message so that
    // existing callers using `e.message` still get a meaningful string.
    super(problem.detail ?? problem.title ?? `Request failed (${status})`)
    this.name = 'ApiError'
    this.problem = problem
    this.status = status
    // Restore prototype chain for instanceof checks across compilation targets.
    Object.setPrototypeOf(this, new.target.prototype)
  }

  /** Shorthand for problem.title */
  get title(): string | undefined {
    return this.problem.title
  }

  /** Shorthand for problem.detail */
  get detail(): string | undefined {
    return this.problem.detail
  }

  /** Shorthand for problem.type */
  get type(): string | undefined {
    return this.problem.type
  }

  /** Shorthand for problem.errors (ASP.NET Core validation) */
  get errors(): Record<string, string[]> | undefined {
    return this.problem.errors
  }
}

/** Returns true when the response Content-Type indicates problem+json. */
export function isProblemJson(res: Response): boolean {
  return (res.headers.get('Content-Type') ?? '').includes('application/problem+json')
}

/**
 * Parses a problem+json body and returns an ApiError.
 * Never throws — falls back to a synthetic ApiError on parse failure.
 */
export async function parseProblemDetail(res: Response): Promise<ApiError> {
  try {
    const problem = (await res.json()) as ProblemDetail
    return new ApiError(problem, res.status)
  } catch {
    return new ApiError({ title: `Request failed`, status: res.status }, res.status)
  }
}

/**
 * Throws an ApiError (for problem+json responses) or a plain Error for any
 * non-2xx response. Does nothing if the response is ok.
 *
 * Use this helper when working directly with `apiFetch` responses that bypass
 * `apiJson`.
 */
export async function throwForStatus(res: Response): Promise<void> {
  if (isProblemJson(res)) {
    throw await parseProblemDetail(res)
  }

  if (res.ok) return

  // Attempt to extract a message from a generic JSON body.
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
  throw new Error(msg)
}