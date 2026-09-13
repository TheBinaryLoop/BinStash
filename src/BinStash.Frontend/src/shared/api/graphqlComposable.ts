import { CombinedGraphQLErrors, CombinedProtocolErrors, ServerError } from '@apollo/client/errors'
import { apolloClient } from '@/shared/api/apolloClient'
import { useTenantStore } from '@/stores/tenant'

type GraphqlRunOptions = {
  tenantScoped?: boolean
  tenantId?: string | null
}

function buildContext(options?: GraphqlRunOptions): { uri?: string } {
  if (!options?.tenantScoped) return {}

  const tenantId = options.tenantId ?? useTenantStore().currentTenantId
  if (!tenantId) {
    throw new Error('No tenant selected.')
  }

  return {
    uri: `/graphql?tenantId=${encodeURIComponent(tenantId)}`,
  }
}

export function normalizeGraphqlError(error: any, fallbackMessage = 'GraphQL request failed.'): Error {
  // Apollo Client 4 removed `ApolloError`. Errors now arrive as distinct classes
  // (CombinedGraphQLErrors / CombinedProtocolErrors / ServerError), each with a
  // static `.is()` guard, so the old `graphQLErrors` / `networkError` lookups
  // would never match and every message would degrade to the generic fallback.
  if (CombinedGraphQLErrors.is(error)) {
    return new Error(error.errors[0]?.message || fallbackMessage)
  }

  if (CombinedProtocolErrors.is(error)) {
    return new Error(error.errors[0]?.message || fallbackMessage)
  }

  if (ServerError.is(error)) {
    return new Error(error.bodyText || error.message || fallbackMessage)
  }

  return new Error(error?.message || fallbackMessage)
}

export async function runQuery<TData = any, TVariables extends Record<string, any> = Record<string, any>>(
  query: any,
  variables?: TVariables,
  options?: GraphqlRunOptions,
): Promise<TData> {
  try {
    const result = await apolloClient.query<TData, TVariables>({
      query,
      variables: variables as TVariables,
      fetchPolicy: 'no-cache',
      context: buildContext(options),
    })

    return result.data as TData
  } catch (error) {
    throw normalizeGraphqlError(error)
  }
}

export async function runMutation<TData = any, TVariables extends Record<string, any> = Record<string, any>>(
  mutation: any,
  variables?: TVariables,
  options?: GraphqlRunOptions,
): Promise<TData> {
  try {
    const result = await apolloClient.mutate<TData, TVariables>({
      mutation,
      variables: variables as TVariables,
      context: buildContext(options),
    })

    return result.data as TData
  } catch (error) {
    throw normalizeGraphqlError(error)
  }
}
