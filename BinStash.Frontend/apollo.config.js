// apollo.config.js
module.exports = {
  client: {
    service: {
      name: 'binstash',
      // URL to the GraphQL API
      url: 'http://localhost:5173/graphql',
      localSchemaFile: './schema.graphql',
    },
    // Files processed by the extension
    includes: [
      'src/**/*.vue',
      'src/**/*.js',
    ],
  },
}