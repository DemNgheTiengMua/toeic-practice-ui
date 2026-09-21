---
name: TOEIC Practice
description: Exam-hall signage — flat painted placards, hard painted keylines, bold condensed caps, and a door sign that reads open or shut.
colors:
  sign-red: "#b31217"
  sign-blue: "#0b4ea2"
  sign-gold: "#f2b705"
  sign-green: "#0a6b3c"
  sign-blue-deep: "#093d80"
  sign-red-deep: "#8c0e12"
  ink: "#14181d"
  wall: "#eef0f1"
  painted: "#ffffff"
  line-soft: "#ccd2d6"
  ink-muted: "#5c6570"
  warning-field: "#8a5a00"
  info-ink: "#08407f"
  success-ink: "#08562f"
  warning-ink: "#6f4800"
  primary-soft: "#e8eef7"
  primary-soft-border: "#b9cbe6"
  success-soft: "#e6f1ea"
  success-soft-border: "#b4d4c2"
  warning-soft: "#fdf3d9"
  warning-soft-border: "#e8cf8f"
  danger-soft: "#fbe9e9"
  danger-soft-border: "#e5b6b6"
  track: "#dfe3e6"
  overlay: "rgba(20, 24, 29, .58)"
  on-sign-blue-muted: "#cfe0fb"
  on-band-muted: "#c8d8ef"
  on-band-success: "#9fe8bd"
  on-band-danger: "#ffc2c4"
  on-ink-muted: "#8f98a3"
  scroll-rail: "#3d454e"
  striped-row: "#f5f6f7"
  featured-panel: "#f4f7fc"
typography:
  display:
    fontFamily: "IBM Plex Mono, ui-monospace, Consolas, monospace"
    fontSize: "56px"
    fontWeight: 700
    lineHeight: 1
    letterSpacing: "-0.03em"
  headline:
    fontFamily: "IBM Plex Sans Condensed, IBM Plex Sans, system-ui, sans-serif"
    fontSize: "28px"
    fontWeight: 700
    lineHeight: 1.15
    letterSpacing: "0.055em"
  title:
    fontFamily: "IBM Plex Sans Condensed, IBM Plex Sans, system-ui, sans-serif"
    fontSize: "22px"
    fontWeight: 700
    lineHeight: 1.15
    letterSpacing: "0.055em"
  body:
    fontFamily: "IBM Plex Sans, system-ui, Segoe UI, Roboto, sans-serif"
    fontSize: "16px"
    fontWeight: 400
    lineHeight: 1.6
    letterSpacing: "normal"
  read:
    fontFamily: "IBM Plex Sans, system-ui, Segoe UI, Roboto, sans-serif"
    fontSize: "18px"
    fontWeight: 400
    lineHeight: 1.7
    letterSpacing: "normal"
  label:
    fontFamily: "IBM Plex Sans Condensed, IBM Plex Sans, system-ui, sans-serif"
    fontSize: "12px"
    fontWeight: 700
    lineHeight: 1.15
    letterSpacing: "0.055em"
  data:
    fontFamily: "IBM Plex Mono, ui-monospace, Consolas, monospace"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.6
    letterSpacing: "normal"
rounded:
  sm: "0"
  md: "0"
  lg: "0"
spacing:
  space-1: "4px"
  space-2: "8px"
  space-3: "12px"
  space-4: "16px"
  space-5: "24px"
  space-6: "32px"
  space-7: "48px"
components:
  button-primary:
    backgroundColor: "{colors.sign-blue}"
    textColor: "{colors.painted}"
    typography: "{typography.label}"
    rounded: "{rounded.sm}"
    padding: "8px 16px"
  button-primary-hover:
    backgroundColor: "{colors.sign-blue-deep}"
    textColor: "{colors.painted}"
  button-plate:
    backgroundColor: "{colors.painted}"
    textColor: "{colors.ink}"
    typography: "{typography.label}"
    rounded: "{rounded.sm}"
    padding: "8px 16px"
  button-plate-hover:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.painted}"
  button-danger:
    backgroundColor: "{colors.sign-red}"
    textColor: "{colors.painted}"
    typography: "{typography.label}"
    rounded: "{rounded.sm}"
    padding: "8px 16px"
  button-danger-hover:
    backgroundColor: "{colors.sign-red-deep}"
    textColor: "{colors.painted}"
  panel:
    backgroundColor: "{colors.painted}"
    textColor: "{colors.ink}"
    rounded: "{rounded.md}"
  panel-header:
    backgroundColor: "{colors.painted}"
    textColor: "{colors.ink}"
    typography: "{typography.label}"
    padding: "12px 24px"
  panel-footer:
    backgroundColor: "{colors.wall}"
    padding: "16px 24px"
  badge:
    backgroundColor: "{colors.painted}"
    textColor: "{colors.ink}"
    typography: "{typography.label}"
    rounded: "{rounded.sm}"
    padding: "2px 8px"
  badge-warning:
    backgroundColor: "{colors.sign-gold}"
    textColor: "{colors.ink}"
  badge-danger:
    backgroundColor: "{colors.sign-red}"
    textColor: "{colors.painted}"
  field-label:
    textColor: "{colors.ink}"
    typography: "{typography.label}"
  field-input:
    backgroundColor: "{colors.painted}"
    textColor: "{colors.ink}"
    typography: "{typography.body}"
    rounded: "{rounded.sm}"
    padding: "8px 12px"
  nav-item:
    backgroundColor: "transparent"
    textColor: "{colors.ink}"
    typography: "{typography.label}"
    padding: "8px 12px"
  nav-item-active:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.painted}"
  stat-readout:
    backgroundColor: "{colors.painted}"
    textColor: "{colors.ink}"
    padding: "12px 16px 16px"
  table-header:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.painted}"
    typography: "{typography.label}"
    padding: "12px"
  modal-header:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.painted}"
    typography: "{typography.label}"
    padding: "12px 24px"
  alert-info:
    backgroundColor: "{colors.primary-soft}"
    textColor: "{colors.info-ink}"
    rounded: "{rounded.sm}"
    padding: "12px 16px"
  state-block-stamp:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.painted}"
    typography: "{typography.label}"
    rounded: "{rounded.sm}"
    padding: "2px 12px"
  cefr-band:
    backgroundColor: "{colors.track}"
    textColor: "{colors.ink-muted}"
    typography: "{typography.label}"
    rounded: "{rounded.sm}"
    height: "34px"
  cefr-band-reached:
    backgroundColor: "{colors.primary-soft}"
    textColor: "{colors.info-ink}"
  cefr-band-current:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.painted}"
---

# Design System: TOEIC Practice

## Overview

**Creative North Star: "Bảng hiệu phòng thi — the exam-room signage"**

This interface is the signage of an examination hall. Everything it has to say, it says the way a building says it: a flat painted placard, a hard painted frame around it, bold condensed capitals, and a door sign that reads open or shut. There is no gradient anywhere in the system, no shadow, no rounded corner, and no blur. A sign is one flat field of colour bounded by a keyline, and that is the whole of its depth model.

The world is Vietnamese and it is not neutral about it. Đỏ `#b31217` is the door, the gate and the refusal; xanh `#0b4ea2` is the learner's own act and carries every primary button in the product; vàng `#f2b705` is attention, the flagged item, the mark; xanh lá `#0a6b3c` is an open condition. They are painted on a wall grey and lettered onto white panels in ink. The composition is a stack of panels and bands hung on that wall, each one framed rather than lifted, each one lettered rather than decorated.

The system is deliberately refusing the pale-blue SaaS card grid that this category ships by default: soft-shadowed white cards, tinted chips, a hero metric, an accent bar down the left edge of every callout. Where the incumbent pattern reaches for a wash, this system inverts; where it reaches for a shadow, this system draws a 2px ink keyline; where it reaches for an icon, this system letters the word. The result is a screen that looks like it was printed and mounted rather than assembled.

Density is high and unapologetic. This is a 200-question exam under a 120-minute clock, and a learner needs the answer grid, the passage, the clock and the save status on one screen at once. Desktop-first at 1440×900 is a recorded trade-off, not an oversight.

**Key Characteristics:**

- Flat painted fields, hard painted keylines, zero radius, zero shadow, zero gradient.
- Three type voices split by job: a reading face, a signage face, and a monospace that cannot lie about its width.
- Active state inverts to a solid ink block; it never tints.
- Colour is a second layer of information, always paired with a word or a number.
- Depth is the keyline hierarchy — 2px ink frame, 1px soft interior rule — plus one dark scrim under dialogs.
- The world has no icon set. A sign says the word.

## Colors

A four-field signage palette — red, blue, gold, green — painted on a wall grey, with a full neutral ramp for lettering and interior rules. Every colour in the system is one of those four fields, the wall, the ink, or a derived wash of one of them.

### Primary

- **Sign Blue** (`#0b4ea2`): the learner's own act. Every primary button, every link, the top band, the score placard, the current question's ring, and the `is-reached` wash on the CEFR bar. If a control does the thing the learner came to do, it is this blue. Hover deepens to `#093d80` rather than lightening — a painted field gets darker under the hand, not brighter.

### Secondary

- **Sign Gold** (`#f2b705`): attention and the mark. The flagged question in the navigator, the warning timer plate, the marked column in a chart, the `average` severity bar, and the text selection highlight. Gold is the one field that takes ink lettering rather than white — white on gold is 2.1:1.

### Tertiary

- **Sign Red** (`#b31217`): the door, the gate, the refusal. The danger button, the critical timer plate, a wrong answer, an invalid field, the fallback marker on an unrecognised state. Hover deepens to `#8c0e12`.
- **Sign Green** (`#0a6b3c`): open; the condition is met. A correct answer, an approved KYC application, a paid order, the `saved` autosave status, the `strong` severity bar, a completed step.

### Neutral

- **Ink** (`#14181d`): all lettering on paper, every structural keyline, the active nav block, table headers, modal headers, the state switcher plate. Ink is also a *field*, not only a text colour — the world's way of saying "this one" is a solid ink block with white caps on it.
- **Wall** (`#eef0f1`): the ground everything is mounted on. Page background and panel footers.
- **Painted** (`#ffffff`): a painted panel; content is lettered onto this. Cards, questions, inputs, the default button plate.
- **Line Soft** (`#ccd2d6`): the 1px interior rule. Dividers *inside* a panel, never the panel's own frame.
- **Ink Muted** (`#5c6570`): secondary text — hints, timestamps, table captions, chart values. 6.0:1 on white, 5.4:1 on the wall.
- **Track** (`#dfe3e6`): the unfilled portion of a progress bar and the background of the CEFR band track.
- **Overlay** (`rgba(20, 24, 29, .58)`): the scrim under a modal. It is the only translucent value in the system.

### Status washes and their inks

A full painted field shouts. Where the surface must stay quiet — an answer explanation, an informational alert, a disabled row — the system uses a pale wash of one of the four fields, bounded by a keyline of the field's own colour, with a darkened rendition of that colour as the lettering. `#e8eef7` / `#b9cbe6` / `#08407f` for blue, `#e6f1ea` / `#b4d4c2` / `#08562f` for green, `#fdf3d9` / `#e8cf8f` / `#6f4800` for gold, `#fbe9e9` / `#e5b6b6` / `#8c0e12` for red.

### Lettering on a coloured band

The topbar is a sign-blue field and the modal header is an ink field. Both are bands whose own text is white, so the paper-tuned neutrals and status inks cannot be reused on them — the muted grey falls to 1.5:1 on sign-blue and the status inks to about 1.2:1. Each band has its own light renditions: `#c8d8ef`, `#9fe8bd`, `#ffc2c4` on the blue band; `#8f98a3` on the ink band; `#cfe0fb` for the caption inside the score placard.

### Named Rules

**The One Flat Field Rule.** A sign's field is one flat colour bounded by a painted keyline. No gradient, no tint, no second colour inside the frame, no blur beneath it. If a surface needs to separate itself from what is behind it, it gets a heavier keyline, not a lighter shadow.

**The Field-Is-Not-Lettering Rule.** The four sign colours at full strength are field colours only; they carry white or ink lettering on top. When the colour itself has to *be* the lettering — a status word, an error message, a link on paper — use the darkened rendition (`#8a5a00` for gold, `#08407f` for blue, `#08562f` for green, `#8c0e12` for red). Gold at `#f2b705` is never text.

**The Inversion Rule.** Active state inverts; it does not tint. A selected option, the current step, the active nav item, the current page number and the focused table row become solid ink blocks with white lettering. A pale blue wash means "related to", never "currently selected".

## Typography

**Display Font:** IBM Plex Mono (with `ui-monospace`, Consolas)
**Body Font:** IBM Plex Sans (with `system-ui`, Segoe UI, Roboto)
**Signage Font:** IBM Plex Sans Condensed (with IBM Plex Sans, system-ui)
**Label/Mono Font:** IBM Plex Sans Condensed for labels; IBM Plex Mono for figures

**Character:** One superfamily, three cuts, and the split is functional rather than decorative. This is an exam, so a single screen has to carry three different kinds of reading at once: sustained prose read against the clock, chrome that has to be recognised in a glance, and figures that must not move while you look at them. Plex Sans is the reading voice; Plex Sans Condensed set in uppercase and tracked is the signage voice — what a room number and a section placard are set in; Plex Mono is the data voice.

All three are self-hosted as woff2 in the latin *and* vietnamese subsets. Cyrillic and Greek are dropped. The Vietnamese subset is kept deliberately even though the copy is English now: the product is Vietnamese, and the type system must survive the copy moving back without a rewrite. Self-hosting rather than linking is the same argument — a monospace whose digits are the same width on every machine, and a condensed cut that is actually condensed on macOS and Android as well as Windows, are not things a fallback stack can be trusted to supply.

### Hierarchy

- **Display** (700, 56px, 1, −0.03em): the total score on the result placard. One instance per screen, on its own sign-blue field, and it is the largest thing in the product.
- **Headline** (700, 28px, 1.15, 0.055em, uppercase): `h1`. A placard line — screen titles, section heads.
- **Title** (700, 22px, 1.15, 0.055em, uppercase): `h2`. A second placard line, one step down.
- **Body** (400, 16px, 1.6): the default. Panel copy, table cells, option text, field values.
- **Read** (400, 18px, 1.7): the question stem, at the full measure of 68ch. The leading is deliberately looser than body — this line is scanned back and forth between the passage and the options, and tighter leading makes that return sweep miss. Part 7 passages use the same leading at 14px inside a bordered, independently scrolling block.
- **Label** (700, 12px, 1.15, 0.055em, uppercase): buttons, badges, field labels, nav items, table headers, stat labels, catalog chips. Short strings, scanned as shapes.
- **Data** (400 or 700, 12–14px, 1.6, tabular numerals): the clock, the 200-cell answer grid, save status, page numbers, chart values, prices.

### Named Rules

**The Width-Is-Meaning Rule.** Anything a learner checks, counts or compares is set in the face that cannot lie about its width. A countdown whose digits change width jitters every second it runs; a grid of question numbers only reads as a grid if every cell is the same width. The clock, the score, the answer navigator, the pagination and the chart values are all IBM Plex Mono with `font-variant-numeric: tabular-nums`.

**The Caps-On-A-Ground Rule.** Uppercase belongs on a ground — a label, a badge, a button, a nav item, a table header — where the string is short enough to be scanned as a shape. It does not belong on a sentence. Any element carrying a full phrase drops to sentence case in the condensed bold face: the state block's title ("You have not answered any question in this exam" is 47 characters), the stepper's step text, the catalog's screen names, and the descriptor after a field label. The signage voice survives in the condensed face; only the shouting stops.

## Layout

A two-row shell. The top row is the painted band — sign-blue, full-bleed, lettered in white, closed by a 2px ink rule. The second row holds an optional 240px sidebar and the main region. The sidebar is a white panel with a 2px ink keyline on its inner edge; the main region is wall grey with 24px of padding.

Content is centred in a container of `max-width: 1180px`, or 460px for the narrow auth and gate screens. Below that, two breakpoints at **1024px** and **640px**. At 1024 the sidebar collapses to a full-width row (its right keyline becomes a bottom one) and three-column grids become two; the exam layout collapses its 300px question navigator to a single column and unsticks it. At 640 everything goes to one column, the topbar wraps, the table scrolls inside itself rather than dragging the page sideways, and flex rows that hold two controls wrap.

The exam screen is its own grid: `minmax(0, 1fr) 300px`, with the navigator sticky at `top: 16px`. The answer navigator is a 10-column grid of square cells; the CEFR bar is a six-column grid whose column ratios are the real score ranges (`110fr 105fr 325fr 235fr 160fr 45fr`) so the marker's position is readable — except below 640px, where the bands go equal so the `C1` label (12px on a 360px phone at its true proportion) has room at its normal size.

Spacing is a 4px scale: 4 · 8 · 12 · 16 · 24 · 32 · 48. Panel padding is 24px, panel header and footer padding 12–16px by 24px, control padding 8px by 16px, and the 12/16 pair is the workhorse inside a panel.

### Named Rules

**The Load-Order Rule.** A media query does not raise specificity; only load order and the selector's own weight decide. A breakpoint that overrides a rule belongs beside that rule, in the same file and after it — never in an earlier stylesheet where an equal-specificity rule loses at every viewport and the breakpoint reads as if it worked. The exam layout's collapse lives in `components.css` next to the grid it overrides, not in `base.css` with the others.

**The State Group Rule.** State visibility is a testable spec, not a style. Each state declares four rule groups: hide every `.only-*` by doubling the class to reach (0,2,0) — enough to beat a component's own `display` — show the matching one with `display: revert`, show the shared `.multi-state[data-show~="x"]` elements the same way, and re-state `display: grid` for `.modal-overlay.only-*`, because `revert` restores the user agent's `block` and pins the dialog to the top-left corner. Do not add a state without all four.

## Elevation & Depth

**There are no shadows in this system.** `--shadow-sm` and `--shadow-md` are both `none`, and no rule in the stylesheet emits a `box-shadow` outside the state switcher's inset fallback ring. Depth is carried entirely by the keyline hierarchy: a 2px ink frame is a sign's painted border, a 1px soft rule is an interior division, and the two are never mixed on the same edge. A modal does not float above the page; it sits behind a `rgba(20, 24, 29, .58)` scrim and inside a heavy ink frame, which is how a placard is distinguished from the wall it is mounted on.

Layering is tonal, not optical. The wall is `#eef0f1`, a panel is `#ffffff`, a band is `#0b4ea2` or `#14181d`. Because the ground and the panel differ by only a few percent of lightness, the keyline is doing the entire job of separation — remove it and the layout dissolves. That is the intended trade.

### Named Rules

**The Keyline-Not-Blur Rule.** Separation is drawn, never blurred. 2px ink for a sign's own frame, 1px soft for a rule inside it. Never a shadow, never a blur, never a translucent panel.

**The Band-Letters-Its-Own-Contents Rule.** A coloured band — the sign-blue topbar, the ink modal header — inverts the page, so any component that expects a paper background must be re-stated for it. An `.app-topbar a` selector outranks `.btn` and will turn a button's ink label white on its own white field at 1.0:1, i.e. an invisible label on every screen that carries the band. Restate the button, the chip, the timer plate and the status inks for each band, and give the band its own light renditions of the muted and status colours.

## Shapes

Square. Every corner in the system is `border-radius: 0`, and `--radius-sm`, `--radius-md` and `--radius-lg` are all `0` so that no future component can round one by accident. A sign has corners.

The recurring silhouette is the **placard**: a rectangle bounded by a 2px ink keyline, one flat field inside it, lettering centred or left-aligned within. It appears as the button, the card, the badge, the alert, the modal, the price card, the toolbar, the question block, the state switcher and the empty-state stamp. Two keyline weights exist and they mean different things: 2px ink is the sign's own frame, 1px soft is a rule drawn *inside* one.

Two dashed-keyline shapes mark something not yet filled: the ID-photo placeholder (at the true 1.586 card ratio) and the file-upload drop zone. The dash is the only ornament the world permits, and it is reserved for that one meaning.

### Named Rules

**The Square-Corner Rule.** Radius is zero, everywhere, including inputs, badges, modals and the progress bar. A rounded corner is the single fastest way to break this world.

**The Dashed-Means-Unfilled Rule.** A dashed keyline appears only where content is missing — an upload target, a photo placeholder. It never appears on a real surface.

## Components

### Buttons

- **Shape:** square plate, 2px ink keyline, zero radius. Uppercase condensed caps at 14px, tracked 0.055em, weight 700.
- **Default (plate):** white field, ink keyline, ink label. On hover the plate inverts to solid ink with a white label — the button *is* the sign, and hovering flips it.
- **Primary:** solid sign-blue field, sign-blue keyline, white label. Hover deepens the field to `#093d80` and the keyline with it. Every primary action in the product is this blue.
- **Danger:** solid sign-red field with a white label. It carries its own hover (`#8c0e12`) because the generic `.btn:hover` would flip it to ink-on-white and the white label would vanish at 1.1:1.
- **Ghost:** transparent field and keyline, ink label; hover draws the ink frame in.
- **Sizes:** `--lg` is 12px/24px padding at 16px; the default is 8px/16px at 14px; `--sm` is 4px/12px at 12px.
- **Disabled:** `opacity: .45`, `cursor: not-allowed`, `pointer-events: none`.
- **Focus:** the global `:focus-visible` — a 3px sign-blue outline at 2px offset. Never removed, never replaced by a colour change.

### Badges

A badge is a small painted plate, not a pale chip: solid field, 1px ink keyline, 12px condensed caps, tabular numerals. `success` is sign-green with a white label, `warning` is sign-gold with an **ink** label (white on gold is 2.1:1), `danger` sign-red and `info`/`primary` sign-blue, both with white labels. `muted` stays a white plate with a soft keyline and muted lettering — it carries facts, not judgments.

### Cards / Panels

- **Corner Style:** square (0).
- **Background:** painted white. The footer drops to the wall grey to close the panel.
- **Border:** 2px solid ink on the panel itself; the header is closed by a 2px ink rule below it, the footer by a 1px soft rule above it.
- **Shadow Strategy:** none — see Elevation & Depth. The frame is the structure.
- **Internal Padding:** 24px in the body; 12–16px by 24px in the header and footer.
- **Header:** the header's label is uppercase condensed caps, not a heading level — a panel header is a placard strip.

### Inputs / Fields

- **Style:** white field, 2px ink keyline, square, 16px body type. The label sits above it in 12px uppercase condensed caps.
- **Focus:** the field's keyline turns sign-blue *and* the global focus outline appears. Focus is never colour-only.
- **Invalid:** the keyline turns sign-red and a red error line appears below at 12px/600.
- **Labels with a descriptor:** the descriptor after the name drops to sentence case, weight 400 and muted — "Part 2 — 25 questions (question-response, 3 answers)" is 52 characters and cannot be set in uppercase.

### Navigation

The sidebar is a painted panel of stacked tabs. Each item is uppercase condensed caps at 14px, tracked 0.03em, with a transparent 1px keyline that becomes visible on hover. The active item inverts to a solid ink block with white lettering. Group headings above a cluster are 12px caps at 0.08em in muted ink. Below 1024px the whole sidebar becomes a full-width row under the topbar.

### Alert

A notice placard, not a callout: a full painted keyline in the variant's colour around a pale wash of it, 2px on all four sides. It was a 4px coloured left bar, which is the most recognisable tell of the category this world refuses; the colour now reads as the sign's frame rather than as a side tab. Every alert flips into view on the system's one authored animation.

### Signature — The State Block

The empty, error and blocked screens share one component: a small ink stamp above a sentence-case title, a description at the 68ch measure, and the way forward. The stamp is a **word in a painted box**, not a pictograph — `EMPTY`, `FAILED`, `EXPIRED`, `BLOCKED`. The world has no icon set; a sign says the word. The title is deliberately sentence case at 18px in the condensed bold face, because these are the screens where the reader is already stuck and uppercase measurably slows reading.

### Signature — The CEFR Band Bar

Six bands in the order the standard fixes them — `<A1` · `A1` · `A2` · `B1` · `B2` · `C1` — inside a 2px ink frame, each band's width proportional to its real score range on the 10–990 scale. Reached bands take the primary-soft wash with `#08407f` lettering; the current band inverts to solid ink with white. A mono tick row below shares the same grid so the numbers line up with the band edges they mark. The whole bar is `role="img"` with an `aria-label` naming the current band and score; the ticks are `aria-hidden`.

**The Band-Order Rule.** The first band is *below A1* (10–119), not A1. Off by one band and the learner is shown the wrong level.

### Signature — The Answer Navigator

A 10-column grid of square cells, one per question, grouped by part with a heading naming the part and its question range. Default cells are muted; answered cells invert to solid ink; marked cells are sign-gold; the current cell takes a sign-blue ring. The whole grid is mono and tabular so the columns stay true.

**The Count-Is-Dynamic Rule.** Part 2 has three options; every other part has four. Never assume four. The same applies to the option list and to any per-part count in a heading.

### Signature — The Score Placard

Not the hero-metric template. The total sits on its own solid sign-blue field as a 56px mono figure with a caption in `#cfe0fb`, and the two section splits are ruled readouts beside it — one big number with two labelled stats is precisely the shape being refused. Below 640px it stacks, and the blue field's right keyline becomes a bottom one.

### Other components

`.stat` is a ruled readout — a 2px ink rule above a mono value — not a tile. `.chart` is painted columns with a hard keyline rising off a baseline rule, the flagged column in gold. `.table` is the ledger: an ink header band, 1px soft rules between rows, tabular numerals throughout. `.stepper`, `.pagination`, `.progress`, `.kyc-photo`, `.explain`, `.price-card`, `.toolbar`, `.upload` and the catalog all follow the same placard discipline.

## Do's and Don'ts

### Do:

- **Do** give every surface a 2px ink keyline for its own frame and a 1px soft rule for divisions inside it, and never mix the two weights on one edge.
- **Do** invert to a solid ink block with white lettering for the selected, current or active item.
- **Do** set every figure a learner might compare — clock, score, navigator cell, page number, chart value, price — in IBM Plex Mono with `font-variant-numeric: tabular-nums`.
- **Do** pair every colour-coded signal with a word or a number. Progress bars carry their percentage; severity bars carry their label; correct and wrong answers carry their own words.
- **Do** drop to sentence case in the condensed bold face for any string longer than a few words — state titles, stepper text, catalog names, label descriptors.
- **Do** keep the topbar's and the modal header's components restated for their own band, with the light renditions of the muted and status colours.
- **Do** keep the four state-visibility rule groups together when adding a state, including the `display: grid` restatement for `.modal-overlay.only-*`.
- **Do** keep a breakpoint beside the rule it overrides.

### Don't:

- **Don't** use a gradient, a `box-shadow`, a `backdrop-filter`, or a non-zero `border-radius` anywhere. The tokens for radius and shadow are zeroed precisely so this cannot happen by accident.
- **Don't** put a coloured `border-left` or `border-right` above 1px on a card, alert, list item or callout. The alert used to be a 4px left bar; it is the category's most recognisable tell.
- **Don't** use a coloured field as lettering at full strength. Gold `#f2b705` is never text; use `#8a5a00`. Blue, green and red follow the same rule.
- **Don't** use a Unicode glyph or an emoji as an icon. The world has no icon set — a sign says the word. The modal close is the word "Close", not a ✕.
- **Don't** set a full sentence in uppercase, and don't put uppercase tracking on sentence-case text: `text-transform` and `letter-spacing` both have to be explicitly reset, or the element-level rule in `base.css` wins.
- **Don't** introduce a pale wash to mean "selected". A wash means "related to" or "reached"; selection inverts.
- **Don't** use a system display face as the display voice. IBM Plex is self-hosted so this cannot drift.
- **Don't** put a kicker or eyebrow line above a heading, number a section unless the sequence carries information, or reach for a modal for a task that needs neither interruption nor protected focus.
- **Don't** stand a soft-shadowed rounded rectangle in for content — a sparkline, a progress ring, or a decorative stat tile. Readouts are ruled and figures are mono.
- **Don't** treat the credit balance, the clock, or the correct answer as the client's own state. The server owns time, answers and balance; the UI displays them.
