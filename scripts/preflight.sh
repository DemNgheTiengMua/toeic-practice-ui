#!/usr/bin/env bash
# scripts/preflight.sh - everything a fresh session must know, derived from live sources.
#
# WHY THIS EXISTS. A compacted session forgets its working memory but keeps the repository, so any
# "remember to..." instruction eventually fails. This script does not remember anything: it READS the
# state from git and from the issue tracker every time it runs, so it cannot go stale and cannot be
# forgotten. Run it as the first command of every session, before reading a file or running a test.
#
# Usage:  bash scripts/preflight.sh
set -uo pipefail
cd "$(dirname "$0")/.." || exit 1

hr() { printf '%s\n' "------------------------------------------------------------"; }

echo "=== 1. where the tree is ==="
echo "branch: $(git rev-parse --abbrev-ref HEAD)"
echo "HEAD:   $(git log --oneline -1)"
echo "dirty:  $(git status --short | wc -l) file(s)"
git status --short | head -20 | sed 's/^/        /'

hr
echo "=== 2. open tickets (derived, never remembered) ==="
if command -v gh >/dev/null 2>&1; then
  gh issue list --repo DemNgheTiengMua/toeic-practice-ui --state open --limit 30 2>/dev/null \
    | awk -F'\t' '{printf "  #%-4s %-60.60s  [%s]\n", $1, $3, $4}'
else
  echo "  gh not on PATH - open the tracker manually"
fi

hr
echo "=== 3. the last twelve commits, newest first ==="
git log --oneline -12 | sed 's/^/  /'

hr
echo "=== 4. what is left, from docs/audit/remaining.md ==="
if [ -f docs/audit/remaining.md ]; then
  sed 's/^/  /' docs/audit/remaining.md
else
  echo "  (no docs/audit/remaining.md - write it before you stop working)"
fi

hr
echo "=== 5. MANDATORY before reading source: graphify ==="
cat <<'EOF'
  AGENTS.md section 2 is not advisory. Before you read a file:

    graphify explain "src/Services/<File>.cs::<Symbol>"   # neighbours, degree, file:line -> prints an ID
    graphify affected <id> --depth 2                       # what breaks if this changes (needs the ID)
    graphify god-nodes --top 15                            # the hubs, once per wave

  A grep returns text that looks like evidence and is not. `affected` is the only thing between a
  deletion and a self-inflicted outage.
EOF
if command -v graphify >/dev/null 2>&1; then
  echo
  echo "  graphify is on PATH."
  if [ -f graphify-out/manifest.json ]; then
    built=$(python -c "import json;print(json.load(open('graphify-out/manifest.json')).get('built_at_commit','')[:12])" 2>/dev/null || echo "?")
    head_now=$(git rev-parse --short=12 HEAD)
    echo "  graph built at: ${built:-?}   HEAD: ${head_now}"
    [ "$built" = "$head_now" ] || echo "  STALE - run: graphify update .   (AST only, no API cost)"
  else
    echo "  no graph yet - run: graphify update ."
  fi
else
  echo
  echo "  graphify NOT on PATH. Build/refresh it before trusting any structural claim."
fi

hr
echo "=== 6. the two commands that prove the product, and their expected shape ==="
cat <<'EOF'
  bash docs/schema-tests/run-all.sh
      -> must print "=== summary: N checks, N pass, 0 fail" and exit 0

  dotnet test tests/Services.Tests --nologo -- xUnit.MaxParallelThreads=1
      -> must print "Failed: 0". CHECK THE TOTAL against the previous run: a total that moved without a
         commit that added tests is a stale process, not a suite (AGENTS.md section 6).

  Filtered while working; the full suite only at the end. It takes ~24 minutes.
EOF
