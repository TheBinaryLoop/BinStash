import { ApiError } from './http'

/**
 * Normalises the three error shapes the app can see — GraphQL errors, REST
 * problem+json, and plain exceptions — into one message fit for a toast.
 */
export function errorMessage(error: unknown, fallback = 'Something went wrong.'): string {
  if (!error) return fallback

  if (error instanceof ApiError) return error.message

  const graphQlErrors = (error as { errors?: Array<{ message?: string }> }).errors
  if (Array.isArray(graphQlErrors) && graphQlErrors[0]?.message) {
    return graphQlErrors[0].message
  }

  const legacy = (error as { graphQLErrors?: Array<{ message?: string }> }).graphQLErrors
  if (Array.isArray(legacy) && legacy[0]?.message) return legacy[0].message

  if (error instanceof Error && error.message) return error.message

  return fallback
}

/** True when the failure is an authorization refusal rather than a bug or outage. */
export function isForbidden(error: unknown): boolean {
  if (error instanceof ApiError) return error.status === 403
  const errors = (error as { errors?: Array<{ extensions?: { code?: string } }> }).errors
  return Array.isArray(errors) && errors.some((e) => e?.extensions?.code === 'FORBIDDEN')
}
