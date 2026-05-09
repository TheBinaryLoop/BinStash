import type { ApolloQueryResult, FetchResult } from '@apollo/client/core'
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
  const graphQlMessage = error?.graphQLErrors?.[0]?.message
    ?? error?.networkError?.result?.errors?.[0]?.message
    ?? error?.networkError?.message
    ?? error?.message

  return new Error(graphQlMessage || fallbackMessage)
}

export async function runQuery<TData = any, TVariables extends Record<string, any> = Record<string, any>>(
  query: any,
  variables?: TVariables,
  options?: GraphqlRunOptions,
): Promise<TData> {
  try {
    const result: ApolloQueryResult<TData> = await apolloClient.query<TData, TVariables>({
      query,
      variables: variables as TVariables,
      fetchPolicy: 'no-cache',
      context: buildContext(options),
    })

    return result.data
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
    const result: FetchResult<TData> = await apolloClient.mutate<TData, TVariables>({
      mutation,
      variables: variables as TVariables,
      context: buildContext(options),
    })

    return result.data as TData
  } catch (error) {
    throw normalizeGraphqlError(error)
  }
}
