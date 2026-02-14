import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueJsx from '@vitejs/plugin-vue-jsx'
import VueDevTools from 'vite-plugin-vue-devtools'

// Change export default defineConfig({ ... }) to use a function so we can access 'mode'
export default defineConfig(({ mode }) => {
  const plugins = [
    vue(),
    vueJsx(),
  ];

  // ONLY add the devtools plugin if we are in development mode
  if (mode === 'development') {
    plugins.push(VueDevTools());
  }

  return {
    plugins: plugins,
    resolve: {
      alias: {
        '@': fileURLToPath(new URL('./src', import.meta.url))
      }
    }
  }
})