# The audit campaign

`docs/audit/screenshots/` holds browser screenshots taken by the E2E suite. Each campaign is its own
directory, and inside it each role is a directory, and inside that each flow is a directory:

```
docs/audit/screenshots/
  full-audit/
    admin/            exams-and-studio/ curriculum/ operations/ administration/
    learner-history/  home/ study/ sitting-and-results/ money-and-mentors/ account/
    learner-fresh/    (same flows, mostly empty states)
    mentor/           ...
    signed-out/       public/
    permission/       refusals in both directions
```

## Why campaigns are separate directories

A campaign answers one question at one moment. Keeping `full-audit` apart from earlier per-issue
directories means a reviewer can point at one tree and know every image in it came from the same seed,
the same commit and the same run. Mixing them would silently compare a screen from one commit against a
screen from another.

## Taking a pass

```sh
scripts/seed-manual-test.sh fresh      # reset + seed + build + start both apps
E2E_BASE_URL=http://localhost:5000 \
  dotnet test tests/WebMVC.E2ETests \
  --filter "FullyQualifiedName~FullAuditCampaignTests" --nologo
```

The run takes a few minutes and overwrites the images rather than adding new ones, so a directory's
contents always describe one run. Compare by opening the tree; the images are full-page, so a screen
taller than the viewport is one image.

## What the images are evidence of

They prove a screen can be reached and drawn with the seeded data. They are not pixel assertions — the
content assertions live in the journeys beside them, and a capture suite that also asserted would be two
tests disagreeing about one fact.

The one exception is money. `ScreenshotCapture` fails a capture that finds an amount rendered in a
shortened form (`20.2M ₫`), because a shortened amount cannot equal the amount in the database and so
cannot be audited as financially correct. Money goes through `CurrencyFormatter`, which states the exact
figure and renders a negative with a true minus sign.

A route that answers non-2xx is still captured, with its status in the filename (`...--HTTP-500.png`).
A missing image would hide a broken route; a picture of the error is what a reviewer needs.

## Known limits

- The sitting journeys (`/exams/{id}/take`, `/placement/take`, `/writing/take`, `/speaking/take`) mutate
  seeded state, so this campaign captures the screens around them — instructions, history, result,
  review — rather than opening a sitting. Starting one would change the data the next campaign captures.
- `/admin/items` has no GET page; the pool is read through an exam's studio. `/admin/imports` is a modal
  on the exam list, not a route.
- The mentor account (`mentor.khoa@demo.local`) holds the `learner` role with an extra mentor profile, so
  its surface overlaps the learner's by design. It is captured separately because a defect that only
  appears for a learner with a mentor profile is invisible to a campaign that signs in once.
