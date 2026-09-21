# Finish review — prototype

Substitution disclosure: this harness has no subagent capability, so the `finish-reviewer` role ran inline; I stepped out of the build work for this pass and reviewed from the files only.

Missing inputs, named above the sections: the world ships no QUALITY BAR card and no `worlds/` entry, so OWN-WORLD in `.impeccable/surfaces/prototype.md:100–107` was the only world statement; `.impeccable/build`, `.impeccable/mocks`, `.impeccable/review/diff` and `hero-repro.png` do not exist, and `.impeccable/live/annotations/` and `live/sessions/` are empty — all expected for a code-led build with no comp and no plate set. No hook or detector findings were supplied.

ship

## persistence

Pass. The world holds across all 35 screens and all 143 states.

- The token layer is the single source: `tokens.css` is the only file declaring colour, and `base.css` / `components.css` reference it by `var()` throughout. The one seam is `#3d454e`, written literally twice for the scrollbar rail (`base.css:32`, `base.css:35`) instead of being tokenised — consistent in value, but the only untokenised colour in the system.
- 143 state combinations driven through a live browser: 0 contrast defects, 0 console errors, 0 warnings. Every state of every page resolved its `data-states` machine and rendered.
- The state machine itself is intact: 35 HTML files, 143 states, 26 distinct state names, and `index.html` as the one page carrying no `data-states` attribute.
- Mobile collapse is handled, not abandoned: at a 370px viewport `docScrollW === bodyScrollW === clientWidth`, so nothing scrolls sideways at page level, and the ≤640px table containment (`overflow-x: auto`) holds the wide admin tables inside their own box.

## fidelity

Element matrix. No contradicted row.

- Wall-grey ground, painted panels, ink lettering — match. `--wall #eef0f1`, `--painted #ffffff`, `--ink #14181d`; 0 contrast defects across 143 state combinations.
- Sign-blue header band — match. `--sign-blue #0b4ea2`, present at the top of both required captures.
- Three type voices — match. 10 self-hosted Plex woff2 (latin + vietnamese subsets); `document.fonts` reports a loaded face for each of Sans, Sans Condensed and Mono; `tabular-nums` in 14 rules carries the data voice.
- Sign lettering — match. `h1, h2 { text-transform: uppercase; letter-spacing: var(--tracking-sign) }` at `base.css:15–22`; `h3` deliberately stays sentence case.
- Square corners — match. `--radius-sm/md/lg` all `"0"` and `border-radius: 0` written literally 30 times.
- Keyline, not blur — match. The only `box-shadow` in the entire system is the inset keyline ring on `.state-switcher--unknown` (`base.css:257`); `--shadow-sm/md` are `none`.
- Flat paint — match. 0 gradients and 0 `backdrop-filter` across all three stylesheets; `clip-path` appears twice and both are the `sr-only` idiom.
- CEFR Band Bar — match. Six bands over 10–990 at the brief's thresholds, grid `110fr 105fr 325fr 235fr 160fr 45fr`.
- Answer Navigator, Score Placard — match. `placard-flip` (`components.css:305,315`) is the one authored motion moment.
- Browser surfaces — match. `::selection` gold on ink, `caret-color` sign-blue, scrollbar track and thumb themed, `:focus-visible` 3px ring at 2px offset, `text-underline-offset: 3px` — all six of the devices the craft floor names, themed from the palette.
- Reduced motion — match. `base.css:298` collapses every animation and transition.
- Vietnamese proper nouns in an English UI — match. 37 visible hits, every one a personal name (Nguyễn Văn A, Trần Thị Bình, Lê Minh Cường, Phạm Thu Hà) appearing as data, not as interface copy. The English directive governs the chrome; the names are correctly Vietnamese, and they are what makes the Vietnamese subset load-bearing.
- `part1-sample.svg` — adaptation. The only non-font asset in the build. An inline SVG self-labelled "Part 1 photo (placeholder)", referenced from two pages as a stand-in for Part 1 photography. Its caption is set in `font-family="sans-serif"` because an SVG loaded through `<img>` is an isolated document that cannot see the page's `@font-face` — the one place the type voice breaks, and it breaks structurally rather than by oversight. The product's own "no real ETS content" constraint makes a labelled placeholder the correct choice here.
- State Block — added without approval. Not named in the brief; introduced to satisfy the brief's own state machine and the craft floor's states requirement. Reported, not blocking.
- `index.html` catalog — added without approval. A 35th screen beyond the brief's screen list. Reported, not blocking.

## ceiling

Not reached. Native devices the build leaves unused:

- `text-wrap: balance` — 0 uses. The craft floor asks for balanced headings; every placard line wraps on the UA default.
- `::placeholder` — 0 uses. Placeholder text takes the UA grey rather than a tone from the palette, on the one surface where the field is otherwise fully themed.
- `color-scheme: light` — 0 uses. On a dark-mode OS the native date picker and select popup render dark against a light page.
- `::backdrop` and the native `<dialog>` element — 0 uses. Modals are `div[role="dialog"]`, so top-layer stacking, `inert` background and focus trapping are hand-rolled.
- `overflow-wrap` / `hyphens` — 0 uses. Long CCCD numbers and email addresses in the admin table have no break rule.
- `::marker`, `:target`, `scroll-behavior`, `font-optical-sizing`, `hanging-punctuation` — all 0 uses.
- `accent-color` — 0 uses, but deliberate: native controls are visually hidden and replaced by a drawn `.option__marker`, so there is nothing to tint.

## material_fixes

None. Every candidate resolved to an authorised adaptation or a bounded structural limit rather than owed work: the brief's "no webfont" clause was superseded by the user's explicit answer choosing self-hosted Plex and is already recorded as authorised drift in `DESIGN.md`; the `part1-sample.svg` caption font is bounded by `<img>` isolation; the two untokenised scrollbar greys are a hygiene seam, not a visible defect; the arrows in `← Previous question` / `Next question →` are typographic punctuation inside labelled buttons, not glyph icons; and the dead tokens (`--radius-*`, `--shadow-*`, `--space-8`, `--text-display`, `--leading-snug`, `--tracking-caps-sm`) are declared-but-unreferenced, of which the zeroed radius and shadow pairs are load-bearing as guards on the world.

One finding owed a verdict and carried in the return rather than as a fix: the FINISH clause's "every shipping raster carrying its provenance" has no subject on this build, because the prototype ships zero rasters. `part1-sample.svg` is an inline SVG and `favicon.svg` is the only other image-like asset; both are text, and both are self-describing.

## keep

Keep the flat field and the keyline-not-blur refusal — the world is one painted surface, and the first added gradient, shadow or rounded corner ends it.
