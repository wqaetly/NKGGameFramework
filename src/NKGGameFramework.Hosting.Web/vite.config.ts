import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  const debugApi = env.NKG_DEBUG_API ?? 'http://localhost:5000';

  return {
    plugins: [react()],
    server: {
      port: 5173,
      proxy: {
        '/_nkg/debug': {
          target: debugApi,
          changeOrigin: true,
        },
      },
    },
    preview: {
      port: 4173,
    },
  };
});
