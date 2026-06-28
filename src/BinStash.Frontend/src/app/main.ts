import { createApp, h, provide } from 'vue'
import { createPinia } from 'pinia'
import { DefaultApolloClient } from '@vue/apollo-composable'
import { router } from './router/index'
import App from './App.vue'
import { apolloClient } from '@/shared/api/apolloClient'

import '@/assets/styles/style.css'


const app = createApp({
    setup() {
        // Provide the Apollo Client instance to the app
        provide(DefaultApolloClient, apolloClient)
    },
    render: () => h(App),
})
app.use(createPinia())
app.use(router)
app.mount('#app')
