/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5080',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },
  test: {
    // Sem isto o Vitest troca todo CSS por vazio, inclusive o "?raw" que os
    // testes de paleta e de menu recolhido leem (src/estilos.test.ts).
    css: true,
  },
});
