import { ApiError } from '../shared/api/http'

export type FieldErrorMap = Record<string, string>

export type ParsedApiValidation = {
  generalError: string | null
  fieldErrors: FieldErrorMap
}

function normalizeKey(key: string): string {
  return key
    .trim()
    .replace(/^\$\.?/, '')
    .replace(/:/g, '.')
    .replace(/\[(\d+)\]/g, '.$1')
    .replace(/\.{2,}/g, '.')
    .replace(/^\./, '')
    .toLowerCase()
}

export function parseApiValidationError(error: unknown, fallbackMessage: string): ParsedApiValidation {
  if (!(error instanceof ApiError)) {
    return {
      generalError: (error as any)?.message ?? fallbackMessage,
      fieldErrors: {},
    }
  }

  const fieldErrors: FieldErrorMap = {}
  if (error.errors) {
    for (const [rawKey, messages] of Object.entries(error.errors)) {
      const msg = Array.isArray(messages) ? messages.find(Boolean) : undefined
      if (!msg) continue

      const normalized = normalizeKey(rawKey)
      fieldErrors[normalized] = msg

      // Add suffix aliases so backend keys like "Email:Shared:SupportEmail"
      // can still match frontend lookups like "shared.supportEmail".
      const parts = normalized.split('.')
      for (let i = 1; i < parts.length; i++) {
        const suffix = parts.slice(i).join('.')
        if (!fieldErrors[suffix]) fieldErrors[suffix] = msg
      }
    }
  }

  const generalError =
    error.detail ??
    error.title ??
    (Object.keys(fieldErrors).length > 0 ? 'Please correct the highlighted fields.' : fallbackMessage)

  return {
    generalError,
    fieldErrors,
  }
}

export function findFieldError(fieldErrors: FieldErrorMap, ...candidateKeys: string[]): string | null {
  for (const key of candidateKeys) {
    const direct = fieldErrors[key]
    if (direct) return direct

    const normalized = normalizeKey(key)
    const normalizedMatch = fieldErrors[normalized]
    if (normalizedMatch) return normalizedMatch
  }

  return null
}