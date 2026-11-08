"""Schema mutation proofs for #237.

Each mutation edits docs/schema-changes-237.sql, runs domain45-mastery.sql, and restores the file. A
guard that stays green under its mutation is not load-bearing.

Run from the repo root:

    python docs/schema-tests/prove-237/prove-guard-237-schema.py no-observation-minimum

Mutations, and the guard each one proves:

  no-state-transition-guard  -> the transition table (the `untested -> mastered` jump test)
  no-mastered-gate           -> entering mastered needs the threshold AND the count
  no-observation-minimum     -> ONLY the count half; the eleven-answer case, whose estimate is 0.867.
                                Four answers would NOT prove this - they are refused by the threshold
                                alone (0.75 < 0.80), which is why the fixture uses eleven.
  no-single-writer-guard     -> the ownership check, including the inherited-name case
"""
import pathlib, subprocess, sys

DELTA = pathlib.Path("docs/schema-changes-237.sql")

MUTATIONS = {
    # Neuter the transition trigger's transition-table refusal. (Renaming the trigger does NOT disable
    # it - a trigger still fires under any name - so the mutation replaces the THROW with a PRINT.)
    "no-state-transition-guard": (
        """    THROW 50114, @msg, 0;""",
        """    PRINT @msg;""",
    ),
    # Keep the transition table but drop the mastered gate: a threshold-only guard.
    "no-mastered-gate": (
        """      AND (m.Probability < 0.80 OR m.ObservationCount < m.MinimumObservations)""",
        """      AND (m.Probability < 0.0 AND m.ObservationCount < 0)""",
    ),
    # Keep the gate but drop the count half only.
    "no-observation-minimum": (
        """      AND (m.Probability < 0.80 OR m.ObservationCount < m.MinimumObservations)""",
        """      AND (m.Probability < 0.80)""",
    ),
    # Neuter the single-writer refusal by value.
    "no-single-writer-guard": (
        """    THROW 50112, 'The per-cell mastery has exactly one writer, CapabilityWriter. A write declaring another writer (or none) is refused.', 1;""",
        """    PRINT 'single-writer guard disabled';""",
    ),
}


def run_domain45():
    r = subprocess.run(
        ["C:\\Program Files\\Git\\bin\\bash.exe", "-c",
         "cd '/c/Users/Nguoi Viet Chim Tay/.local/share/opencode/worktree/f5d847/cefr-237-mastery' "
         "&& TEST_DB_NAME=EE237Mut bash docs/schema-tests/run-one.sh docs/schema-tests/domain45-mastery.sql"],
        capture_output=True, text=True,
    )
    return r.returncode, (r.stdout + r.stderr)


def main():
    name = sys.argv[1]
    original, mutated = MUTATIONS[name]
    text = DELTA.read_text(encoding="utf-8")
    if original not in text:
        print(f"!! {name}: target not found")
        return 2

    DELTA.write_text(text.replace(original, mutated), encoding="utf-8")
    try:
        code, out = run_domain45()
        print(f"=== {name}: exit {code}")
        for line in out.splitlines():
            if "EXPECT_REJECT FAILED" in line or "Msg 50" in line or "PASS" in line:
                print("  ", line.strip())
        print("  VERDICT:", "RED as expected" if code != 0 else "!! GREEN - guard not load-bearing")
    finally:
        DELTA.write_text(text, encoding="utf-8")
        print("  restored", DELTA)


if __name__ == "__main__":
    sys.exit(main())
