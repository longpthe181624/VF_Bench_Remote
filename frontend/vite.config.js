import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
export default defineConfig({
  base: '/app/',
  plugins: [react(), tailwindcss()],
  server: {port:5173,strictPort:true,proxy:{'/api':{target:process.env.BENCH_API_URL||'http://127.0.0.1:5000',changeOrigin:true},'/hub':{target:process.env.BENCH_API_URL||'http://127.0.0.1:5000',ws:true}}},
})
