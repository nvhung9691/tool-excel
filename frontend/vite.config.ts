import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Build thang ra wwwroot cua project API -> deploy 1 tien trinh `dotnet run` duy nhat,
// server khong can Node. Node chi can luc build.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    // `npm run dev` proxy /api sang API dang chay o cong 5199 (dotnet run --urls).
    proxy: {
      '/api': { target: 'http://localhost:5199', changeOrigin: true },
    },
  },
})
