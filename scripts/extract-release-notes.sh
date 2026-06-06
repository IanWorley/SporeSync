#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  scripts/extract-release-notes.sh --tag TAG --output FILE

Extracts release notes for TAG from CHANGELOG.md. If no matching changelog
section exists, falls back to commit subjects since the previous git tag.
USAGE
}

tag=""
output=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --tag)
      tag="${2:-}"
      shift 2
      ;;
    --output)
      output="${2:-}"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ -z "$tag" ]]; then
  echo "--tag is required." >&2
  usage >&2
  exit 1
fi

if [[ -z "$output" ]]; then
  echo "--output is required." >&2
  usage >&2
  exit 1
fi

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"

version="${tag#v}"
base_version="${version%%-*}"

extract_section() {
  local section_version="$1"

  if [[ ! -f CHANGELOG.md ]]; then
    return
  fi

  awk -v section="## [${section_version}]" '
    index($0, section) == 1 {
      in_section = 1
      next
    }
    in_section && /^## \[/ {
      exit
    }
    in_section {
      print
    }
  ' CHANGELOG.md | sed '/./,$!d'
}

tmp_output="$(mktemp)"
trap 'rm -f "$tmp_output"' EXIT

extract_section "$version" > "$tmp_output"
if [[ ! -s "$tmp_output" && "$base_version" != "$version" ]]; then
  extract_section "$base_version" > "$tmp_output"
fi

if [[ -s "$tmp_output" ]]; then
  cp "$tmp_output" "$output"
  exit 0
fi

previous_tag="$(git describe --tags --abbrev=0 "${tag}^" 2>/dev/null || true)"
if [[ -n "$previous_tag" ]]; then
  commit_range="${previous_tag}..${tag}"
else
  commit_range="$tag"
fi

{
  printf '## Changes in %s\n\n' "$tag"

  if [[ -n "$previous_tag" ]]; then
    printf 'Commits since %s:\n\n' "$previous_tag"
  else
    printf 'Commits included in this release:\n\n'
  fi

  if ! git log --no-merges --pretty=format:'- %s (%h)' "$commit_range"; then
    printf 'No matching changelog section or commit range was found for %s.\n' "$tag"
  fi

  printf '\n'
} > "$output"
