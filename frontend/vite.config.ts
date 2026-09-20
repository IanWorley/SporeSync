import react from '@vitejs/plugin-react';
import { defineConfig, loadEnv } from 'vite';

const DEFAULT_BACKEND_URL = 'http://127.0.0.1:8080';
const DEV_PORT = 5173;

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), 'BACKEND_');

  return {
    plugins: [react()],
    server: {
      host: '127.0.0.1',
      port: DEV_PORT,
      strictPort: true,
      proxy: {
        '^/api(?:/|$)': {
          target: process.env.BACKEND_URL ?? env.BACKEND_URL ?? DEFAULT_BACKEND_URL,
        },
      },
    },
  };
});
