# Mutation check for gate two (#195). Applies one deliberate defect at a time to GateTwo.cs,
# rebuilds, runs the gate two tests, and reports whether anything caught it. Reverts every change.
#
# Usage: bash scripts/prove-gate2-guards.sh
#
# This is the check AGENTS.md calls for: a guard that no test fails when it is broken is not a
# guard. Each mutation below is the defect a reviewer would plausibly ship.

set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

FILE="src/BusinessObjects/Authoring/GateTwo.cs"
BACKUP="$(mktemp)"
cp "$FILE" "$BACKUP"
trap 'cp "$BACKUP" "$FILE"; rm -f "$BACKUP"' EXIT

pass=0
fail=0

run_mutation () {  # $1 = label, $2 = sed expression
  local label="$1" expr="$2"
  cp "$BACKUP" "$FILE"
  if ! sed -i "$expr" "$FILE"; then
    echo "SKIP $label (sed did not match)"
    return
  fi
  if cmp -s "$FILE" "$BACKUP"; then
    echo "SKIP $label (mutation changed nothing)"
    return
  fi

  if dotnet test tests/Services.Tests --filter "FullyQualifiedName~GateTwo" --nologo -v q >/dev/null 2>&1; then
    echo "MISS $label - no test failed"
    fail=$((fail + 1))
  else
    echo "caught $label"
    pass=$((pass + 1))
  fi
}

# --- gate one's requirement: a threshold must not drift from the specification -------------------
run_mutation "D0.2: A2 reading word ceiling 180 -> 181" \
  's|\[CefrLevel.A2\] = new((60, 95), (120, 180)|[CefrLevel.A2] = new((60, 95), (120, 181)|'

run_mutation "D0.2: A1 listening envelope copied from reading" \
  's|\[CefrLevel.A1\] = new((40, 65), (60, 100)|[CefrLevel.A1] = new((60, 100), (60, 100)|'

run_mutation "D0.1: B2 MLTU ceiling 17.0 -> 17.5" \
  's|GateTwoBound.Between(13.5, 17.0), GateTwoBound.Between(1.6, 2.0)|GateTwoBound.Between(13.5, 17.5), GateTwoBound.Between(1.6, 2.0)|'

run_mutation "D0.1: C1 clauses/T-unit strict floor becomes inclusive" \
  's|GateTwoBound.Above(2.0))|GateTwoBound.Between(2.0, double.PositiveInfinity))|'

run_mutation "D0.2: the listening MLTU gap leaves the 15-20% window" \
  's|ListeningSubordinationGap = 0.175|ListeningSubordinationGap = 0.30|'

run_mutation "D0.1: B1 lexical floor 90 -> 80" \
  's|3000, 90.0, 0)|3000, 80.0, 0)|'

# --- the arithmetic: the wrong denominator is the defect the ticket names -----------------------
run_mutation "MLTU measured as mean SENTENCE length" \
  's|public double MeanTUnitLength => TUnits == 0 ? 0 : (double)Words / TUnits;|public double MeanTUnitLength => Sentences == 0 ? 0 : (double)Words / Sentences;|'

run_mutation "coverage divided by content words instead of every token" \
  's|public double LexicalCoverage => Words == 0 ? 1 : (double)InListTokens / Words;|public double LexicalCoverage => Words == 0 ? 1 : Math.Min(1, (double)InListTokens / (Words * 0.8));|'

run_mutation "a subordinate clause counted as its own T-unit" \
  's|^            if (coordination)$|            if (true)|'

run_mutation "a coordinated clause never counted as a T-unit" \
  's|^            if (coordination)$|            if (false)|'

# --- the acoustic rule --------------------------------------------------------------------------
run_mutation "WER compared against a rounded percentage" \
  's|if (error.Rate <= MaximumWordErrorRate)|if (Math.Round(error.Rate * 100, 1) <= MaximumWordErrorRate * 100)|'

run_mutation "an unavailable transcription treated as a pass" \
  's|                GateTwoRules.TranscriptUnavailable,|                GateTwoRules.NotProfiled,|'

run_mutation "WER normalises nothing, so capitals are substitutions" \
  's|var heard = Tokens(hypothesis).Select(Normalise).Where(word => word.Length > 0).ToList();|var heard = Tokens(hypothesis).ToList();|'

run_mutation "C2 treated as covering no words instead of all of them" \
  's|if (!Table.TryGetValue(band, out var row) \|\| row.LexicalListSize is null)|if (!Table.TryGetValue(band, out var row))|'

echo
echo "=== $pass mutations caught, $fail missed"
[ "$fail" -eq 0 ]
