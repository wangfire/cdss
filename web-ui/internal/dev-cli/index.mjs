import { spawn } from 'node:child_process'
import { intro, outro, select, log, multiselect, spinner } from '@clack/prompts'
import { rm } from 'node:fs/promises'
import { existsSync } from 'node:fs'
import { glob } from 'glob'
import { resolve, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = dirname(fileURLToPath(import.meta.url))
const rootDir = resolve(__dirname, '../..')

// 需要预构建的包列表（按依赖顺序排列，先构建依赖项）
const requiredPackages = [
  { 
    name: '@tabtab/utils', 
    path: 'packages/utils',
    distCheck: 'dist/index.mjs'
  },
  { 
    name: '@tabtab/ui', 
    path: 'packages/ui',
    distCheck: 'dist/vite.mjs'
  }
]

// 缓存类型定义
const cacheTypes = [
  { 
    name: 'vite', 
    label: 'Vite 缓存', 
    hint: '依赖预构建缓存',
    patterns: ['node_modules/.vite', 'apps/*/node_modules/.vite', 'packages/*/node_modules/.vite'] 
  },
  { 
    name: 'vitest', 
    label: 'Vitest 缓存', 
    hint: '测试运行缓存',
    patterns: ['node_modules/.vitest', 'apps/*/node_modules/.vitest', 'packages/*/node_modules/.vitest'] 
  },
  { 
    name: 'tsdown', 
    label: 'tsdown 缓存', 
    hint: '库构建缓存',
    patterns: ['.tsdown', 'packages/*/.tsdown'] 
  },
  { 
    name: 'tsbuildinfo', 
    label: 'TypeScript 增量缓存', 
    hint: '*.tsbuildinfo 文件',
    patterns: ['**/*.tsbuildinfo'] 
  },
  { 
    name: 'dist', 
    label: '构建产物', 
    hint: 'dist 目录',
    patterns: ['apps/*/dist', 'packages/*/dist'] 
  },
  { 
    name: 'node_modules', 
    label: 'node_modules', 
    hint: '所有依赖目录（需重新安装）',
    patterns: ['node_modules', 'apps/*/node_modules', 'packages/*/node_modules', 'internal/*/node_modules'],
    isNodeModules: true
  },
]

// 应用列表
const apps = [
  { name: 'admin', label: 'Admin', command: 'pnpm dev', cwd: 'apps/admin' },
]

// 解析命令行参数
const args = process.argv.slice(2)
const isCleanMode = args.includes('--clean') || args.includes('-c')
const isCleanAll = args.includes('--all') || args.includes('-a')
const isDeepClean = args.includes('--deep') || args.includes('-d')

// 确保依赖包已构建
async function ensurePackagesBuilt() {
  const packagesToBuild = []
  
  for (const pkg of requiredPackages) {
    const distPath = resolve(rootDir, pkg.path, pkg.distCheck)
    if (!existsSync(distPath)) {
      packagesToBuild.push(pkg)
    }
  }
  
  if (packagesToBuild.length > 0) {
    log.info('检测到依赖包未构建，正在自动构建...')
    
    for (const pkg of packagesToBuild) {
      const s = spinner()
      s.start(`正在构建 ${pkg.name}...`)
      
      await new Promise((resolvePromise, reject) => {
        const child = spawn('pnpm build', {
          shell: true,
          stdio: 'pipe',
          cwd: resolve(rootDir, pkg.path),
          env: { ...process.env, FORCE_COLOR: '1' },
        })
        
        let stderr = ''
        child.stderr?.on('data', (data) => {
          stderr += data.toString()
        })
        
        child.on('close', (code) => {
          if (code === 0) {
            s.stop(`${pkg.name} 构建完成`)
            resolvePromise()
          } else {
            s.stop(`${pkg.name} 构建失败`)
            if (stderr) {
              log.error(stderr)
            }
            reject(new Error(`${pkg.name} 构建失败，退出码: ${code}`))
          }
        })
      })
    }
    
    log.success('所有依赖包构建完成！')
  }
}

// 清理缓存函数
async function cleanCache(selectedTypes) {
  const s = spinner()
  s.start('正在清理缓存...')
  
  let cleanedCount = 0
  const errors = []
  const hasNodeModules = selectedTypes.includes('node_modules')
  
  for (const type of selectedTypes) {
    const cacheType = cacheTypes.find(c => c.name === type)
    if (!cacheType) continue
    
    for (const pattern of cacheType.patterns) {
      try {
        const files = await glob(pattern, { 
          cwd: rootDir,
          absolute: true,
          ignore: ['**/node_modules/**/node_modules/**']
        })
        
        for (const file of files) {
          try {
            await rm(file, { recursive: true, force: true })
            cleanedCount++
          } catch (err) {
            // 忽略不存在的文件
            if (err.code !== 'ENOENT') {
              errors.push(`${file}: ${err.message}`)
            }
          }
        }
      } catch (err) {
        errors.push(`glob ${pattern}: ${err.message}`)
      }
    }
  }
  
  if (errors.length > 0) {
    s.stop(`清理完成，但有 ${errors.length} 个错误`)
    for (const error of errors) {
      log.error(error)
    }
  } else {
    s.stop(`已清理 ${cleanedCount} 个缓存目录/文件`)
  }
  
  // 如果清理了 node_modules，询问是否重新安装
  if (hasNodeModules) {
    const shouldInstall = await select({
      message: '是否立即重新安装依赖？',
      options: [
        { label: '是，执行 pnpm install', value: true },
        { label: '否，稍后手动安装', value: false },
      ],
    })
    
    if (shouldInstall) {
      log.info('正在安装依赖...')
      const child = spawn('pnpm install', {
        shell: true,
        stdio: 'inherit',
        cwd: rootDir,
        env: { ...process.env, FORCE_COLOR: '1' },
      })
      
      child.on('close', (code) => {
        if (code === 0) {
          outro('清理并重新安装完成！')
        }
        process.exit(code ?? 0)
      })
      return { cleanedCount, shouldExit: true }
    }
  }
  
  return { cleanedCount, shouldExit: false }
}

// 深度清理函数
async function deepClean() {
  const s = spinner()
  s.start('正在删除 node_modules...')
  
  try {
    // 删除根目录和所有子目录的 node_modules
    const nodeModules = await glob('**/node_modules', {
      cwd: rootDir,
      absolute: true,
      onlyDirectories: true
    })
    
    // 添加根目录的 node_modules
    nodeModules.push(resolve(rootDir, 'node_modules'))
    
    for (const dir of nodeModules) {
      try {
        await rm(dir, { recursive: true, force: true })
      } catch (err) {
        if (err.code !== 'ENOENT') {
          log.error(`删除 ${dir} 失败: ${err.message}`)
        }
      }
    }
    
    s.stop('已删除所有 node_modules')
    
    // 重新安装依赖
    log.info('正在重新安装依赖...')
    const child = spawn('pnpm install', {
      shell: true,
      stdio: 'inherit',
      cwd: rootDir,
      env: { ...process.env, FORCE_COLOR: '1' },
    })
    
    child.on('close', (code) => {
      if (code === 0) {
        outro('深度清理完成！')
      }
      process.exit(code ?? 0)
    })
  } catch (err) {
    s.stop('深度清理失败')
    log.error(err.message)
    process.exit(1)
  }
}

// 缓存清理模式
async function runCleanMode() {
  intro('缓存清理工具')
  
  // 深度清理模式
  if (isDeepClean) {
    const confirmed = await select({
      message: '确定要删除所有 node_modules 并重新安装依赖吗？',
      options: [
        { label: '是，执行深度清理', value: true },
        { label: '否，取消操作', value: false },
      ],
    })
    
    if (!confirmed) {
      outro('已取消')
      process.exit(0)
    }
    
    await deepClean()
    return
  }
  
  // 清理所有缓存
  if (isCleanAll) {
    const allTypes = cacheTypes.map(c => c.name)
    const result = await cleanCache(allTypes)
    if (!result.shouldExit) {
      outro('所有缓存已清理完成！')
    }
    return
  }
  
  // 交互式选择清理
  const selectedTypes = await multiselect({
    message: '选择要清理的缓存类型：',
    options: cacheTypes.map(type => ({
      label: type.label,
      value: type.name,
      hint: type.hint,
    })),
    required: false,
  })
  
  if (!selectedTypes || selectedTypes.length === 0) {
    outro('未选择任何缓存类型，退出。')
    process.exit(0)
  }
  
  // 如果选择了 node_modules，给出警告确认
  if (selectedTypes.includes('node_modules')) {
    const confirmed = await select({
      message: '⚠️  删除 node_modules 后需要重新安装依赖，确定继续吗？',
      options: [
        { label: '是，继续清理', value: true },
        { label: '否，取消操作', value: false },
      ],
    })
    
    if (!confirmed) {
      outro('已取消')
      process.exit(0)
    }
  }
  
  const result = await cleanCache(selectedTypes)
  if (!result.shouldExit) {
    outro('缓存清理完成！')
  }
}

// 开发服务器选择模式
async function runDevMode() {
  intro('选择要运行的应用')
  
  // 确保依赖包已构建
  await ensurePackagesBuilt()
  
  const selected = await select({
    message: '请选择要启动的应用：',
    options: apps.map((app) => ({
      label: app.label,
      value: app.name,
    })),
  })
  
  if (!selected) {
    outro('未选择任何应用，退出。')
    process.exit(0)
  }
  
  const app = apps.find((a) => a.name === selected)
  
  log.info(`正在启动 ${app.label}...`)
  
  const child = spawn(app.command, {
    shell: true,
    stdio: 'inherit',
    cwd: app.cwd,
    env: { ...process.env, FORCE_COLOR: '1' },
  })
  
  child.on('close', (code) => {
    process.exit(code ?? 0)
  })
}

// 主入口
if (isCleanMode) {
  runCleanMode()
} else {
  runDevMode()
}
