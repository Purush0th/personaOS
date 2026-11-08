#!/usr/bin/env bash
# Prints the "What changed" part of a release's notes: every commit since the previous tag of the
# same kind, subject in bold and the commit's own explanation under it. GitHub's generated notes
# list merged pull requests only, and this project commits straight to dev, so they came out empty.
#
#   scripts/release-notes.sh <tag> <tag pattern> [path ...]
#   scripts/release-notes.sh v0.1.0-alpha.28 'v*'
#   scripts/release-notes.sh mobile-v0.1.0-alpha.29 'mobile-v*' src/frontend/PersonaOS.Mobile
#
# Paths limit the list to commits that touched them (the phone app's notes skip server-only work).
# Needs the full history and tags (actions/checkout with fetch-depth: 0).
set -euo pipefail

tag="$1"
pattern="$2"
shift 2

# The previous tag of the same kind that has a published release: a tag whose build failed
# (mobile-v0.1.0-alpha.28) has none, and its changes belong in the next release's notes.
previous=""
for candidate in $(git tag --list "$pattern" --merged "$tag^" --sort=-version:refname); do
  if ! command -v gh >/dev/null || gh release view "$candidate" --json tagName >/dev/null 2>&1; then
    previous="$candidate"
    break
  fi
done
range="${previous:+$previous..}$tag"

echo "## What changed"
echo
commits=$(git log --reverse --no-merges --format=%H "$range" -- "$@")
if [ -z "$commits" ]; then
  echo "No changes to this part since ${previous:-the start}."
else
  for commit in $commits; do
    echo "- **$(git show -s --format=%s "$commit")**"
    body=$(git show -s --format=%b "$commit" | sed -e 's/^/  /' -e 's/^[[:space:]]*$//')
    if [ -n "$body" ]; then
      echo
      echo "$body"
    fi
    echo
  done
fi

if [ -n "$previous" ]; then
  echo "All changes: https://github.com/${GITHUB_REPOSITORY:-Purush0th/personaOS}/compare/$previous...$tag"
  echo
fi
