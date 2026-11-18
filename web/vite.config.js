import { defineConfig } from 'vite';

export default defineConfig({
  // GitHub Pages preview uses the repository path; production custom domains set VITE_BASE_PATH=/.
  base: process.env.VITE_BASE_PATH || '/MG-Business-Platform/',
  build: { outDir: 'dist', sourcemap: true },
});
