import { ApolloClient, HttpLink, InMemoryCache, split } from '@apollo/client/core'
import { GraphQLWsLink } from '@apollo/client/link/subscriptions'
import { getMainDefinition } from '@apollo/client/utilities'
import { createClient } from 'graphql-ws'

const httpLink = new HttpLink({
  uri: '/graphql',
  credentials: 'include',
})

const wsProtocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:'
const wsLink = new GraphQLWsLink(
  createClient({
    url: `${wsProtocol}//${window.location.host}/graphql`,
  }),
)

// Route subscriptions over WebSocket, everything else over HTTP
const splitLink = split(
  ({ query }) => {
    const definition = getMainDefinition(query)
    return definition.kind === 'OperationDefinition' && definition.operation === 'subscription'
  },
  wsLink,
  httpLink,
)

export const apolloClient = new ApolloClient({
  link: splitLink,
  cache: new InMemoryCache(),
})
