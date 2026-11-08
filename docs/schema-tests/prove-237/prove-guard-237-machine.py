"""Mutation harness for #237: prove each guard is load-bearing.

Applied one at a time, run the Mastery filter, record red, restore from the in-memory original.
Writing with UTF-8 no BOM from Python avoids the code-page damage PowerShell's Set-Content caused.

Run from the repo root:

    python docs/schema-tests/prove-237/prove-guard-237-machine.py count-half-of-mastered-gate

Mutations, and the test each one proves:

  count-half-of-mastered-gate     -> MasteryCellMachineTests.The_mastered_state_needs_the_probability_and_the_count_together
                                     and ...Threshold_reached_with_the_count_short_does_not_master_the_cell
  counter-not-reset-by-correct    -> MasteryCellMachineTests.Wrong_then_correct_then_wrong_is_not_a_lapse
  mock-exception-is-any-mock      -> MasteryServiceTests.A_below_level_mock_section_does_not_save_the_cell
                                     (and the another-skill and predating cases)
  decay-fires-on-never-mastered   -> MasteryCellMachineTests.Decay_never_fires_on_a_cell_that_was_never_mastered
  mastered-arm-consults-the-estimate
                                  -> MasteryCellMachineTests.A_single_wrong_answer_never_changes_a_mastered_cell...
  entering-mastered-from-untested-allowed
                                  -> MasteryCellMachineTests.Mastered_is_never_reached_from_untested_without_answers
"""
import pathlib, subprocess, sys

CELL = pathlib.Path("src/BusinessObjects/Practice/MasteryCellMachine.cs")
SERVICE = pathlib.Path("src/Services/Practice/MasteryService.cs")

MUTATIONS = {
    "count-half-of-mastered-gate": (
        CELL,
        """if (probability >= MasterySchedule.MasteredThreshold
            && observationCount >= minimumObservations)""",
        """if (probability >= MasterySchedule.MasteredThreshold)""",
    ),
    "counter-not-reset-by-correct": (
        CELL,
        "var consecutiveWrong = isCorrect ? 0 : current.ConsecutiveWrong + 1;",
        "var consecutiveWrong = current.ConsecutiveWrong + (isCorrect ? 0 : 1);",
    ),
    "mock-exception-is-any-mock": (
        SERVICE,
        """&& demonstrations.Any(d =>
            d.Modality == cellModality
            && d.Level >= cellLevel
            && d.SatAt > practised);""",
        """&& demonstrations.Count > 0;""",
    ),
    "decay-fires-on-never-mastered": (
        CELL,
        """        if (current.State != MasteryState.Mastered)
        {
            return new Step(current, current.State, current.State, current.NextReviewDueAt);
        }

""",
        "",
    ),
    "mastered-arm-consults-the-estimate": (
        CELL,
        """        if (current.State == MasteryState.Mastered)
        {
            if (!isCorrect)""",
        """        if (current.State == MasteryState.Mastered && p >= MasterySchedule.MasteredThreshold)
        {
            if (!isCorrect)""",
    ),
    "entering-mastered-from-untested-allowed": (
        CELL,
        """        if (from == MasteryState.Untested && observationCount == 0)
        {
            return MasteryState.Untested;
        }

""",
        "",
    ),
}


def run_tests():
    r = subprocess.run(
        ["dotnet", "test", "tests/Services.Tests", "--filter", "FullyQualifiedName~Mastery"],
        capture_output=True, text=True,
    )
    out = r.stdout + r.stderr
    failed = [ln.strip() for ln in out.splitlines() if ln.strip().startswith("Failed Services.Tests")]
    return failed, out


def main():
    which = sys.argv[1] if len(sys.argv) > 1 else None
    name = which or list(MUTATIONS)[0]
    path, original, mutated = MUTATIONS[name]

    text = path.read_text(encoding="utf-8")
    if original not in text:
        print(f"!! {name}: target text not found - the guard has drifted")
        return 2

    path.write_text(text.replace(original, mutated), encoding="utf-8")
    try:
        failed, _ = run_tests()
        print(f"=== {name}")
        if not failed:
            print("  !! ALL GREEN - the guard is not load-bearing (or no test covers it)")
        for f in failed:
            print("  FAIL", f)
        print(f"  red count: {len(failed)}")
    finally:
        path.write_text(text, encoding="utf-8")
        print(f"  restored {path}")


if __name__ == "__main__":
    sys.exit(main())
