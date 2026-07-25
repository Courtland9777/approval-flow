import { randomUUID } from 'node:crypto'
import { spawn } from 'node:child_process'
import { createConnection } from 'node:net'
import sql from 'mssql'

const prefix = 'ApprovalFlowE2E_'
const databaseName = `${prefix}${randomUUID().replaceAll('-', '')}`
if (!new RegExp(`^${prefix}[0-9a-f]{32}$`).test(databaseName)) {
  throw new Error('Refusing E2E run because the generated database name is outside the exact allowed pattern.')
}

const password = 'LocalOnly_ApprovalFlow_2026!'
const connectionString =
  `Server=localhost,14333;Database=${databaseName};User Id=sa;Password=${password};TrustServerCertificate=True;Encrypt=True`
const playwrightVersion = '1.61.0'
const containerName = `approvalflow-playwright-${randomUUID().replaceAll('-', '')}`
let browserServer

function startBrowserServer() {
  browserServer = spawn('docker', [
    'run', '--rm', '--init', '--ipc=host',
    '--name', containerName,
    '--network', 'host',
    '--workdir', '/home/pwuser',
    '--user', 'pwuser',
    `mcr.microsoft.com/playwright:v${playwrightVersion}-noble`,
    '/bin/sh', '-c',
    `npx -y playwright@${playwrightVersion} run-server --port 3000 --host 0.0.0.0`,
  ], { stdio: ['ignore', 'inherit', 'inherit'] })
  browserServer.on('error', (error) => {
    console.error(`Playwright browser server failed: ${error.message}`)
  })
}

async function waitForBrowserServer() {
  const deadline = Date.now() + 300_000
  while (Date.now() < deadline) {
    const reachable = await new Promise((resolve) => {
      const socket = createConnection({ host: '127.0.0.1', port: 3000 })
      socket.setTimeout(500)
      socket.once('connect', () => { socket.destroy(); resolve(true) })
      socket.once('timeout', () => { socket.destroy(); resolve(false) })
      socket.once('error', () => resolve(false))
    })
    if (reachable) return
    if (browserServer?.exitCode !== null) {
      throw new Error(`Playwright browser container exited with code ${browserServer?.exitCode}.`)
    }
    await new Promise((resolve) => setTimeout(resolve, 500))
  }
  throw new Error('Timed out waiting for the Playwright browser server.')
}

async function stopBrowserServer() {
  if (!browserServer || browserServer.exitCode !== null) return
  browserServer.kill('SIGTERM')
  await new Promise((resolve) => {
    const timeout = setTimeout(resolve, 10_000)
    browserServer.once('exit', () => { clearTimeout(timeout); resolve() })
  })
}

function runPlaywright() {
  return new Promise((resolve, reject) => {
    const child = spawn('./node_modules/.bin/playwright', ['test'], {
      cwd: process.cwd(),
      stdio: 'inherit',
      env: {
        ...process.env,
        APPROVALFLOW_E2E_DATABASE: databaseName,
        APPROVALFLOW_E2E_CONNECTION: connectionString,
        APPROVALFLOW_E2E_REMOTE: 'true',
        PW_TEST_CONNECT_WS_ENDPOINT: 'ws://127.0.0.1:3000/',
      },
    })
    child.on('error', reject)
    child.on('exit', (code, signal) => resolve({ code: code ?? 1, signal }))
  })
}

async function dropExactDatabase() {
  if (!new RegExp(`^${prefix}[0-9a-f]{32}$`).test(databaseName)) {
    throw new Error(`Refusing to drop database outside exact E2E pattern: ${databaseName}`)
  }
  const pool = await sql.connect({
    server: 'localhost',
    port: 14333,
    database: 'master',
    user: 'sa',
    password,
    options: { encrypt: true, trustServerCertificate: true },
  })
  try {
    await pool.request()
      .input('databaseName', sql.NVarChar(128), databaseName)
      .query(`
        IF DB_ID(@databaseName) IS NOT NULL
        BEGIN
          DECLARE @quotedName nvarchar(258) = QUOTENAME(@databaseName);
          EXEC(N'ALTER DATABASE ' + @quotedName + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE;');
          EXEC(N'DROP DATABASE ' + @quotedName + N';');
        END;
      `)
    const result = await pool.request()
      .input('databaseName', sql.NVarChar(128), databaseName)
      .query('SELECT COUNT(*) AS [count] FROM sys.databases WHERE [name] = @databaseName;')
    if (result.recordset[0].count !== 0) throw new Error(`E2E database still exists: ${databaseName}`)
    console.log(`Verified E2E database removed: ${databaseName}`)
  } finally {
    await pool.close()
  }
}

console.log(`Dedicated E2E database: ${databaseName}`)
let result = { code: 1, signal: undefined }
try {
  startBrowserServer()
  await waitForBrowserServer()
  result = await runPlaywright()
} finally {
  await stopBrowserServer()
  await dropExactDatabase()
}
if (result.signal) console.error(`Playwright ended from signal ${result.signal}`)
process.exitCode = result.code
