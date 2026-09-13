import { ApolloClient, ApolloLink, HttpLink, InMemoryCache, split } from '@apollo/client/core'
import { ErrorLink } from '@apollo/client/link/error'
import { GraphQLWsLink } from '@apollo/client/link/subscriptions'
import { getMainDefinition } from '@apollo/client/utilities'
import { createClient } from 'graphql-ws'

/**
 * Apollo client for the app.
 *
 * Two things here are deliberate corrections of how the previous UI used GraphQL:
 *
 * 1. The normalised cache is ON. The old client ran every operation with
 *    `fetchPolicy: 'no-cache'` behind imperative promise wrappers, which paid the
 *    full cost of GraphQL and Apollo while getting none of the caching,
 *    reactivity or cache-update-on-mutation back.
 * 2. Tenancy travels in the `X-Tenant-Id` header rather than a `?tenantId=` query
 *    parameter. The server's TenantResolutionMiddleware accepts both, but a header
 *    keeps one stable URL for gateways, rate limiting and logs — and lets the cache
 *    be reset per tenant instead of being keyed by URL.
 */

/** Resolved lazily so the store is not imported before Pinia is installed. */
let currentTenantId: string | null = null

export function setApolloTenant(tenantId: string | null) {
  currentTenantId = tenantId
}

export function getApolloTenant() {
  return currentTenantId
}

const httpLink = new HttpLink({
  uri: '/graphql',
  credentials: 'include',
})

const tenantLink = new ApolloLink((operation, forward) => {
  if (currentTenantId) {
    operation.setContext(({ headers = {} }: { headers?: Record<string, string> }) => ({
      headers: { ...headers, 'X-Tenant-Id': currentTenantId },
    }))
  }

  return forward(operation)
})

/** Reported to the app shell so a session that expired mid-session can bounce to sign-in. */
type UnauthorizedHandler = () => void
let onUnauthorized: UnauthorizedHandler = () => {}

export function setUnauthorizedHandler(handler: UnauthorizedHandler) {
  onUnauthorized = handler
}

const errorLink = new ErrorLink(({ error }) => {
  const codes = new Set<string>()

  if (Array.isArray((error as { errors?: unknown[] })?.errors)) {
    for (const entry of (error as { errors: Array<{ extensions?: { code?: string } }> }).errors) {
      if (entry?.extensions?.code) codes.add(entry.extensions.code)
    }
  }

  const statusCode = (error as { statusCode?: number })?.statusCode

  if (statusCode === 401 || codes.has('AUTH_NOT_AUTHENTICATED') || codes.has('UNAUTHENTICATED')) {
    onUnauthorized()
  }
})

const wsLink = new GraphQLWsLink(
  createClient({
    url: () => {
      const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:'
      // The socket cannot set headers, so tenancy falls back to the query parameter here.
      const tenant = currentTenantId ? `?tenantId=${encodeURIComponent(currentTenantId)}` : ''
      return `${protocol}//${window.location.host}/graphql${tenant}`
    },
    // Subscriptions only matter while a job is on screen; don't hold a socket open otherwise.
    lazy: true,
    retryAttempts: 5,
  }),
)

const cache = new InMemoryCache({
  typePolicies: {
    Query: {
      fields: {
        // Relay connections: merge pages instead of replacing, keyed by everything
        // except the cursor so filtering/sorting still starts a fresh list.
        repositories: relayPagination(['where', 'order']),
        tenants: relayPagination(['where', 'order']),
        users: relayPagination(['where', 'order']),
        serviceAccounts: relayPagination(['where', 'order']),
        chunkStores: relayPagination(['where', 'order']),
        backgroundJobs: relayPagination(['jobType', 'chunkStoreId', 'order']),
        auditLog: relayPagination(['where', 'order']),
        instanceAuditLog: relayPagination(['where', 'order']),
      },
    },
    RepositoryGql: {
      fields: { releases: relayPagination(['where', 'order']) },
    },
    // Value objects with no identity of their own must not be normalised, or every
    // repository's chunker config would collide on a single cache entry.
    ChunkStoreChunkerGql: { keyFields: false },
    ChunkStoreBackendSettingsGql: { keyFields: false },
    RepositoryConfigGql: { keyFields: false },
    RepositoryDedupeConfigGql: { keyFields: false },
    TenantUsageGql: { keyFields: ['tenantId'] },
    ReleaseMetricsGql: { keyFields: false },
    RepositoryAccessGql: { keyFields: false },
  },
})

interface Connection {
  __typename?: string
  nodes?: unknown[] | null
  edges?: unknown[] | null
  pageInfo?: Record<string, unknown>
  totalCount?: number
}

function relayPagination(keyArgs: string[]) {
  return {
    keyArgs,
    merge(existing: Connection | undefined, incoming: Connection, { args }: { args: Record<string, unknown> | null }) {
      // A request without a cursor is a fresh first page (initial load or refetch),
      // so it replaces rather than appends — otherwise refetches would duplicate rows.
      if (!args?.after && !args?.before) return incoming

      return {
        ...incoming,
        nodes: [...(existing?.nodes ?? []), ...(incoming.nodes ?? [])],
        edges: [...(existing?.edges ?? []), ...(incoming.edges ?? [])],
      }
    },
  }
}

const isSubscription = ({ query }: { query: Parameters<typeof getMainDefinition>[0] }) => {
  const definition = getMainDefinition(query)
  return definition.kind === 'OperationDefinition' && definition.operation === 'subscription'
}

export const apolloClient = new ApolloClient({
  link: ApolloLink.from([errorLink, tenantLink, split(isSubscription, wsLink, httpLink)]),
  cache,
  defaultOptions: {
    watchQuery: { fetchPolicy: 'cache-and-network', nextFetchPolicy: 'cache-first' },
  },
})

/**
 * Drops all cached data. Called on sign-out and on tenant switch: cached entities
 * are tenant-scoped and must never leak across either boundary.
 */
export async function resetApolloStore() {
  await apolloClient.clearStore()
}
