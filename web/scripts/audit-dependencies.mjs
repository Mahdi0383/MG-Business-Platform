import { spawnSync } from 'node:child_process';

const result = spawnSync('npm', ['audit', '--json'], {
  encoding: 'utf8',
  shell: process.platform === 'win32',
  maxBuffer: 10 * 1024 * 1024,
});

let report;
try {
  report = JSON.parse(result.stdout || '');
} catch {
  process.stderr.write(result.stderr || result.stdout || 'npm audit did not return valid JSON.\n');
  process.exit(result.status || 1);
}

const vulnerabilities = report.vulnerabilities ?? {};
const suppressed = new Set();
const advisoryUrl = 'https://github.com/advisories/GHSA-fc5q-39xc-vjpf';

// This unreviewed advisory currently has no published patched picomatch release.
// Suppress only this exact advisory when it is transitive, then only packages
// whose entire audit finding is caused by an already-suppressed dependency.
// All other high/critical findings remain release blockers.
for (const [name, vulnerability] of Object.entries(vulnerabilities)) {
  const onlyKnownPicomatchAdvisory = name === 'picomatch'
    && vulnerability.isDirect === false
    && Array.isArray(vulnerability.via)
    && vulnerability.via.length > 0
    && vulnerability.via.every(item => typeof item === 'object' && item.url === advisoryUrl);
  if (onlyKnownPicomatchAdvisory) suppressed.add(name);
}

let changed = true;
while (changed) {
  changed = false;
  for (const [name, vulnerability] of Object.entries(vulnerabilities)) {
    if (suppressed.has(name) || !Array.isArray(vulnerability.via) || vulnerability.via.length === 0) continue;
    const onlySuppressedDependencies = vulnerability.via.every(
      item => typeof item === 'string' && suppressed.has(item),
    );
    if (onlySuppressedDependencies) {
      suppressed.add(name);
      changed = true;
    }
  }
}

const blockers = Object.entries(vulnerabilities).filter(([name, vulnerability]) =>
  !suppressed.has(name) && ['high', 'critical'].includes(vulnerability.severity),
);

if (suppressed.size > 0) {
  console.warn(`Documented temporary exception: ${[...suppressed].join(', ')} depend only on ${advisoryUrl}; no upstream patched release is published yet.`);
}

if (blockers.length > 0 || (result.status !== 0 && Object.keys(vulnerabilities).length === 0)) {
  console.error('Dependency audit failed. Unexcepted high/critical findings:');
  for (const [name, vulnerability] of blockers) {
    console.error(`- ${name}: ${vulnerability.severity}; direct=${vulnerability.isDirect}; via=${JSON.stringify(vulnerability.via)}`);
  }
  if (blockers.length === 0) process.stderr.write(result.stderr || 'npm audit failed without a parseable vulnerability list.\n');
  process.exit(1);
}

console.log('Dependency audit passed: no unexcepted high/critical vulnerabilities.');
