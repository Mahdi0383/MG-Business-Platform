#!/usr/bin/env bash
set -euo pipefail

root="${1:-artifacts}"
if [[ ! -d "$root" ]]; then
  echo "::error::Release artifact directory does not exist: $root"
  exit 1
fi

required=(
  "installer/MG-Business-Platform-Setup-win-x64.exe"
  "MG-Business-Platform-win-x64.zip"
  "installer/MG-License-Manager-Setup-win-x64.exe"
  "MG-License-Manager-win-x64.zip"
  "MG-Business-Platform-SOURCE.zip"
  "MG-License-Manager-SOURCE.zip"
)

for relative_path in "${required[@]}"; do
  artifact="$root/$relative_path"
  manifest="$artifact.sha256"

  if [[ ! -f "$artifact" || -L "$artifact" ]]; then
    echo "::error::Required release artifact is missing or is a symlink: $relative_path"
    exit 1
  fi
  if [[ ! -f "$manifest" || -L "$manifest" ]]; then
    echo "::error::Required checksum manifest is missing or is a symlink: $relative_path.sha256"
    exit 1
  fi

  mapfile -t lines < "$manifest"
  if [[ "${#lines[@]}" -ne 1 ]]; then
    echo "::error::Checksum manifest must contain exactly one line: $relative_path.sha256"
    exit 1
  fi

  line="${lines[0]%$'\r'}"
  if [[ ! "$line" =~ ^([[:xdigit:]]{64})[[:space:]][[:space:]](.+)$ ]]; then
    echo "::error::Malformed SHA-256 manifest: $relative_path.sha256"
    exit 1
  fi

  expected="${BASH_REMATCH[1],,}"
  file_name="${BASH_REMATCH[2]}"
  if [[ "$file_name" != "$(basename -- "$relative_path")" ]]; then
    echo "::error::Checksum manifest names an unexpected file: $relative_path.sha256"
    exit 1
  fi

  actual="$(sha256sum -- "$artifact" | cut -d ' ' -f 1)"
  if [[ "${actual,,}" != "$expected" ]]; then
    echo "::error::SHA-256 mismatch: $relative_path"
    exit 1
  fi

  echo "Verified: $relative_path"
done

manifest_count="$(find "$root" -type f -name '*.sha256' -print | wc -l)"
if [[ "$manifest_count" -ne "${#required[@]}" ]]; then
  echo "::error::Expected ${#required[@]} checksum manifests, found $manifest_count."
  exit 1
fi
