import { build, context } from 'esbuild'
import { mkdir, readFile, rm, writeFile } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const projectRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const outputRoot = resolve(projectRoot, '../Tavi.Host/wwwroot')
const watch = process.argv.includes('--watch')
const options = {
  absWorkingDir: projectRoot,
  entryPoints: ['src/main.tsx'],
  bundle: true,
  outdir: resolve(outputRoot, 'assets'),
  entryNames: 'app',
  assetNames: '[name]-[hash]',
  minify: !watch,
  sourcemap: watch,
  target: ['es2022'],
  format: 'esm',
  jsx: 'automatic',
  loader: {
    '.woff': 'file',
    '.woff2': 'file',
    '.ttf': 'file',
  },
}

await rm(outputRoot, { recursive: true, force: true })
await mkdir(resolve(outputRoot, 'assets'), { recursive: true })
const template = await readFile(resolve(projectRoot, 'index.html'), 'utf8')
const html = template.replace('</head>', '    <link rel="stylesheet" href="/assets/app.css" />\n  </head>').replace('</body>', '    <script type="module" src="/assets/app.js"></script>\n  </body>')
await writeFile(resolve(outputRoot, 'index.html'), html)

if (watch) {
  const buildContext = await context(options)
  await buildContext.watch()
  console.log(`Tavi Web 正在监视源文件；请通过 Tavi.Host 地址访问界面。`)
} else {
  await build(options)
}
