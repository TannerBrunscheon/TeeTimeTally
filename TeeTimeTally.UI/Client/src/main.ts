import './assets/main.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'

import App from './App.vue'
import router from './router'
import authentication from './plugins/authentication'

// Import Bootstrap Icons CSS
import 'bootstrap-icons/font/bootstrap-icons.css'

const app = createApp(App)

app.use(createPinia())
app.use(authentication) // This should call store.getUserInfo()
app.use(router)

app.mount('#app')
