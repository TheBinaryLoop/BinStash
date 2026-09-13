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

const shared = {
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
}

const config: CodegenConfig = {
  schema: './schema.graphql',
  documents: ['src/graphql/**/*.graphql'],
  ignoreNoDocuments: true,
  generates: {
    // Schema types (enums, inputs, filter/sort inputs) live on their own. `typescript-operations`
    // emits the schema types an operation touches unless it is told where they already are, so
    // running it in the same file as `typescript` produced a second copy of every enum and input —
    // duplicate identifiers that only a type-checking transform (vitest's) ever complained about.
    './src/graphql/generated/schema.ts': {
      plugins: ['typescript'],
      config: shared,
    },
    './src/graphql/generated/index.ts': {
      plugins: ['typescript-operations', 'typed-document-node'],
      // Resolved against the project root, then re-relativised against this output file.
      config: { ...shared, importSchemaTypesFrom: './src/graphql/generated/schema' },
    },
  },
}

export default config
