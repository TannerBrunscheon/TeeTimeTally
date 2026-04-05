import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueJsx from '@vitejs/plugin-vue-jsx'
// Change export default defineConfig({ ... }) to use an async function so we can access 'mode'
// and dynamically import dev-only plugins without evaluating them during non-dev builds.
export default defineConfig(async ({ mode }) => {
  const plugins: any[] = [
    vue(),
    vueJsx(),
  ];

  // ONLY dynamically import and add the devtools plugin if we are in development mode
  if (mode === 'development') {
    const { default: VueDevTools } = await import('vite-plugin-vue-devtools');
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