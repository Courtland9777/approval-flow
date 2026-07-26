import { execFileSync } from 'node:child_process'

const config = JSON.parse(execFileSync(
  'docker',
  ['compose', 'config', '--format', 'json'],
  { encoding: 'utf8' },
))

const invalid = Object.entries(config.services).flatMap(([serviceName, service]) =>
  (service.ports ?? [])
    .filter((port) => port.host_ip !== '127.0.0.1')
    .map((port) => `${serviceName}:${port.published ?? port.target} (${port.host_ip ?? 'empty host IP'})`))

if (invalid.length > 0) {
  throw new Error(`Compose ports must publish only on 127.0.0.1: ${invalid.join(', ')}`)
}

console.log('Verified all published Compose ports bind only to 127.0.0.1.')
