# docs/schema-tests/fixture-guard.awk — inject a row-count assertion after every fixture
# INSERT ... SELECT, so a statement that matches zero rows aborts naming itself.
#
# Used by run-all.sh and scripts/schema-tests-baseline.sh with `awk -v LABEL=<file> -f`. Both share
# this one copy on purpose: if only one installed the guard, the two commands could disagree about
# which files fail, which is the disagreement #164 exists to end.
#
# Why it is needed: a fixture that matches nothing leaves the variables declared after it NULL, and
# the test's own `THROW 'EXPECT_REJECT FAILED'` then fires. That is the right verdict for the wrong
# reason — it is how domain04 claimed "difficulty 9 accepted" while CK_Questions_Difficulty was in
# fact rejecting 9. ExpectFixtureRows (lib.sql) is what the injected EXEC calls.
#
# A deliberately empty fixture (WHERE 1=0) is left alone: that is how harness-guard-selftest.sql
# proves the guard fires.

# Offset of the ';' that ends this line's statement, or 0. Tracks string literals so a ';' inside a
# literal does not end it, and stops at a -- comment.
function stmt_end(s,   i, c) {
  for (i = 1; i <= length(s); i++) {
    c = substr(s, i, 1)
    if (c == "-" && substr(s, i + 1, 1) == "-" && q == 0) return 0
    if (q == 1) {
      if (c == "'") {
        if (substr(s, i + 1, 1) == "'") { i++; continue }   # '' is an escaped quote
        q = 0
      }
      continue
    }
    if (c == "'") { q = 1; continue }
    if (c == ";") return i
  }
  return 0
}

BEGIN { q = 0; n = 0; in_stmt = 0 }
{
  if (!in_stmt) {
    if ($0 !~ /^[ \t]*INSERT[ \t]+INTO[ \t]/) { print; next }
    in_stmt = 1; q = 0; n = 0; start = NR; empty = 0; stmt = ""
    target = $0
    sub(/^[ \t]*INSERT[ \t]+INTO[ \t]+/, "", target)     # table name, for the message
    sub(/[ \t(].*$/, "", target)
  }
  buf[++n] = $0
  stmt = stmt " " $0
  if ($0 ~ /WHERE 1=0/) empty = 1
  at = stmt_end($0)
  if (at == 0) next
  for (i = 1; i < n; i++) print buf[i]
  guard = (stmt ~ /SELECT/ && !empty) ? \
    " EXEC ExpectFixtureRows N'" LABEL ":" start ": " target "';" : ""
  print substr($0, 1, at) guard substr($0, at + 1)
  in_stmt = 0; q = 0
}
END { if (in_stmt) for (i = 1; i <= n; i++) print buf[i] }
