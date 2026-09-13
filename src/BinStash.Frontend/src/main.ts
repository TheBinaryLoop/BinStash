import { createPinia } from 'pinia'
import { createApp } from 'vue'

import App from './App.vue'
import './assets/styles/index.css'
import { i18n } from './i18n'
import { setUnauthorizedHandler } from './lib/apollo'
import { router } from './router'
import { useAuthStore } from './stores/auth'
import { useTenantStore } from './stores/tenant'

const app = createApp(App)

// No Apollo provide/inject: composables/useGraphql.ts talks to the client singleton
// directly (see the note there on why @vue/apollo-composable is not used).
app.use(createPinia())
app.use(i18n)
app.use(router)

// A session that expires mid-use should land on sign-in rather than fail silently on
// every subsequent query.
setUnauthorizedHandler(() => {
  const auth = useAuthStore()
  if (!auth.isAuthenticated) return

  auth.user = null
  useTenantStore().reset()
  void router.push({ name: 'sign-in', query: { redirect: router.currentRoute.value.fullPath } })
})

app.mount('#app')
