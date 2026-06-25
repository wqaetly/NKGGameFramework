import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const debugApi = 'http://127.0.0.1:5067';

export default defineConfig(() => {
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
    build: {
      rolldownOptions: {
        output: {
          codeSplitting: true,
        },
      },
    },
  };
});
