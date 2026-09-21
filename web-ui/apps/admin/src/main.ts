import { createApp } from 'vue'
import App from './App.vue'
import router from './router'
import { createAppI18n, initI18n } from './i18n'
import { createStore, useLocaleStore, useUserStore } from './stores'
import { useThemeStore } from './stores/theme'
import { http } from '@/api'

import '@tabtab/styles'

async function initApp() {
  const app = createApp(App)
  const pinia = createStore()

  app.use(pinia)

  // Async create i18n instance (preloads all messages)
  const i18n = await createAppI18n()
  app.use(i18n)

  // init i18n internal state
  await initI18n()

  // init language settings
  const localeStore = useLocaleStore()
  await localeStore.init()

  // init theme
  const themeStore = useThemeStore()
  themeStore.initTheme()

  // global 401 handler
  http.setUnauthorizedHandler(() => {
    const userStore = useUserStore()
    if (userStore.isAuthenticated) {
      userStore.clear()
      if (router.currentRoute.value.name !== 'Login') {
        router.push({
          name: 'Login',
          query: { redirect: router.currentRoute.value.fullPath },
        })
      }
    }
  })

  app.use(router)
  app.mount('#app')
}

initApp().catch((error) => {
  console.error('Failed to initialize app:', error)
})
