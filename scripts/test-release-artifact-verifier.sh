#!/usr/bin/env bash
set -euo pipefail

verifier="$(cd "$(dirname "$0")" && pwd)/verify-release-artifacts.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
root="$work/artifacts"

required=(
  "installer/MG-Business-Platform-Setup-win-x64.exe"
  "MG-Business-Platform-win-x64.zip"
  "installer/MG-License-Manager-Setup-win-x64.exe"
  "MG-License-Manager-win-x64.zip"
  "MG-Business-Platform-SOURCE.zip"
  "MG-License-Manager-SOURCE.zip"
)

make_fixture() {
  rm -rf "$root"
  mkdir -p "$root"
  for relative_path in "${required[@]}"; do
    artifact="$root/$relative_path"
    mkdir -p "$(dirname "$artifact")"
    printf 'synthetic release artifact: %s\n' "$relative_path" > "$artifact"
    hash="$(sha256sum -- "$artifact" | cut -d ' ' -f 1)"
    printf '%s  %s\n' "$hash" "$(basename -- "$relative_path")" > "$artifact.sha256"
  done
}

expect_rejected() {
  local description="$1"
  if bash "$verifier" "$root" >/dev/null 2>&1; then
    echo "::error::Verifier accepted invalid fixture: $description"
    exit 1
  fi
  echo "Rejected as expected: $description"
}

make_fixture
bash "$verifier" "$root"

# Content changes after the build must be detected after artifact transfer.
printf 'tampered\n' >> "$root/MG-Business-Platform-win-x64.zip"
expect_rejected "artifact content tampering"

make_fixture
: > "$root/MG-Business-Platform-win-x64.zip.sha256"
expect_rejected "empty checksum manifest"

make_fixture
printf 'deadbeef  MG-Business-Platform-win-x64.zip\n' > "$root/MG-Business-Platform-win-x64.zip.sha256"
expect_rejected "truncated checksum digest"

make_fixture
hash="$(sha256sum -- "$root/MG-Business-Platform-win-x64.zip" | cut -d ' ' -f 1)"
printf '%s  ../MG-Business-Platform-win-x64.zip\n' "$hash" > "$root/MG-Business-Platform-win-x64.zip.sha256"
expect_rejected "manifest path traversal"

make_fixture
cat "$root/MG-Business-Platform-win-x64.zip.sha256" >> "$root/MG-Business-Platform-win-x64.zip.sha256"
expect_rejected "multiple checksum lines"

make_fixture
rm "$root/MG-License-Manager-SOURCE.zip"
expect_rejected "missing release artifact"

make_fixture
printf 'unexpected  file\n' > "$root/unexpected.sha256"
expect_rejected "unexpected extra checksum manifest"

make_fixture
bash "$verifier" "$root"

if [[ -n "${1:-}" ]]; then
  output="$1"
  if [[ -e "$output" || "$output" == "/" || "$output" == "." ]]; then
    echo "::error::Fixture output path must be a new, non-root directory: $output"
    exit 1
  fi
  mkdir -p "$(dirname "$output")"
  cp -a "$root" "$output"
fi

echo "Release artifact verifier contract tests passed."
