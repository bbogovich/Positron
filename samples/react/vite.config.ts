import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
//
// Positron serves the built `dist/` over app://localhost/ with a real origin, so the default
// absolute base ("/") is correct — assets are requested as /assets/... against that origin.
// (Only use a relative base like "./" when serving from a file:// origin, which Positron does not.)
export default defineConfig({
  base: '/',
  plugins: [react()],
})
