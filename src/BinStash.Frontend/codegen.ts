import type { CodegenConfig } from '@graphql-codegen/cli'

/**
 * Generates typed documents from the committed schema.graphql rather than by introspecting a
 * running server, so `pnpm codegen` works offline and in CI. Refresh the schema with `pnpm schema`
 * after changing the server's GraphQL surface.
 *
 * Output is one named TypedDocumentNode per operation (`RepositoriesDocument`, …), which
 * `useQuery`/`useMutation` infer both result and variable types from — so a schema change that
 * breaks a component fails `pnpm typecheck` instead of failing at runtime.
 */
const config: CodegenConfig = {
  schema: './schema.graphql',
  documents: ['src/graphql/**/*.graphql'],
  ignoreNoDocuments: true,
  generates: {
    './src/graphql/generated/index.ts': {
      plugins: ['typescript', 'typescript-operations', 'typed-document-node'],
      config: {
        useTypeImports: true,
        skipTypename: true,
        avoidOptionals: { field: true, inputValue: false, object: false, defaultValue: false },
        enumsAsTypes: true,
        dedupeOperationSuffix: true,
        scalars: {
          UUID: 'string',
          DateTime: 'string',
          Long: 'number',
          UnsignedLong: 'number',
          UnsignedByte: 'number',
          Short: 'number',
          Byte: 'number',
          Any: 'unknown',
        },
      },
    },
  },
}

export default config
