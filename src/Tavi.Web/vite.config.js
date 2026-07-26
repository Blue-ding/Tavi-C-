import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';
var srcPath = path.resolve(__dirname, './src').replace(/\\/g, '/').replace(/#/g, '%23');
export default defineConfig({
    root: process.cwd(),
    plugins: [react()],
    resolve: {
        alias: {
            '@': srcPath,
        },
    },
    server: {
        proxy: {
            '/api': {
                target: 'http://127.0.0.1:5178',
                changeOrigin: true,
            },
        },
    },
    base: '/',
    build: {
        outDir: '../Tavi.Host/wwwroot',
        emptyOutDir: true,
    },
});
