import { createApp, h } from 'vue'
import { createPinia } from 'pinia'
import { router } from './router/index'
import App from './App.vue'
import '@/shared/api/apolloClient'

import '@/assets/styles/style.css'


const app = createApp({
    render: () => h(App),
})
app.use(createPinia())
app.use(router)
app.mount('#app')
