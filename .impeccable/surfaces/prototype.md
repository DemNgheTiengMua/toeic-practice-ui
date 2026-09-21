---
version: 1
slug: "prototype"
primary_target: "prototype"
related_targets: []
---

# Surface brief — TOEIC Practice prototype (re-skin)

## Scope and mode

Whole prototype, 35 static pages: learner (dashboard, practice, exam, result, review,
KYC), admin (KYC review, exams, questions, packages, orders, score conversion), auth,
payment. **Mode: Operate.** Every page is a task surface; the only Persuade element is
the pricing page, which inherits the same world rather than arguing in its own.

## Audience, job, action

Vietnamese self-study learners preparing for TOEIC Listening & Reading, studying for a
score they need for graduation or a job. They arrive to practise by part for free, learn
which parts are weak, verify identity, buy credits, sit a 200-question timed exam, and
read a score converted to CEFR. Admin reviews the KYC queue and authors exams.

## Constraints

- Vietnamese UI copy, English exam content — hard convention (PRODUCT.md).
- Static HTML/CSS/vanilla JS; **no build step, no framework, no npm, no CDN.** Therefore
  system font stacks only; no webfont may be introduced.
- Desktop-first is a recorded trade-off. Unhandled mobile collapse is not — the critique
  found `.grid-3` rendering 23px columns at 390px, which is a defect, not the trade-off.
- The `data-states` / `?state=` machine and every `.only-*` / `.multi-state` rule must keep
  working unchanged: they are the prototype's testable specification.
- No invented claims, no real ETS content, no fabricated testimonials, benchmarks or logos.
- Preserve every class name the 35 pages already use. The re-skin is CSS-first; HTML edits
  are limited to defects (option inputs, radios, live regions, headings) and copy.

## Chosen direction

**Bảng hiệu Việt Nam — the exam-room signage world.** The flat painted placard language of
Vietnamese school and street signage: saturated painted fields, hand-painted keylines, bold
condensed caps, and the flip sign that reads open or shut. Assigned by `concept-seed`,
scope `direction`, mode `operate`, seed key `31ad46c9`, index 7 of my own grounded list.

Seven grounded directions, ordered by how directly the world carries the core loop:

1. Vở ô ly / sổ điểm — the ruled study record (exercise book and mark book as one paper world)
2. Phiếu trả lời trắc nghiệm — the optical answer sheet
3. Phiếu báo điểm có dấu đỏ — the stamped score notice
4. Đề photo ở lò luyện thi — the photocopied practice paper
5. Bảng viết lớp học — the classroom whiteboard
6. Thẻ CCCD gắn chip — the ID card's security printing
7. **Bảng hiệu / biển phòng thi — exam-room and street signage ← assigned**

### Challenger verdicts

Fused with product facts, weighed on audience identification and product clarity only.

- `studio-dumbar-identity` — **declined.** A photographic-plus-saturated-bars identity
  grammar has no purchase on a tool whose content is text and numbers. Kept: hard vector
  geometry as page structure, never floating cards.
- `digital-design-canon-console-dashboard-atmosphere` — **declined.** Dark spatial media
  fights dense reading; this product is passages, tables and timers read for two hours.
  Kept: selection carries physical weight — the current state is filled, not outlined.
- `textiles-weave-drape-fashion-drawcord-transforming-cape` — **competitive.** Transformation
  as a single gesture is exactly what a gate sequence is. Loses audience identification:
  couture silk has no relationship to a TOEIC candidate.
- `kinetic-sculpture-automata-tensegrity-breathing-column` — **declined.** Leader-line
  engineering annotation is costume for a product whose numbers are scores, not forces.
  Kept: every number shows its provenance.
- `signals-instruments-phosphor-terminal-midnight` — **declined.** The terminal register would
  read as a developer tool and fail this audience outright. Kept: the continuous record as a
  native form — attempts, orders and KYC events are entries in one record, not cards.
- `medium-native-hypercard-stack-shoebox` — **competitive.** Browse/author duality maps onto
  this product's learner/admin duality. Loses audience identification: HyperCard is not this
  audience's world. Kept: invert rather than tint for active states.
- **IMPECCABLE'S PICK** — the vở ô ly / sổ điểm ruled-paper world (my #1). Honest risk: a
  paper-and-notebook study app is a familiar shape, though its specific squared-grid-plus-red-
  margin grammar is not the category default. Shown because the user deciding that trade is
  the point.

### Memorable moment

**The gate placard flips.** A blocked state (KYC unverified, no credits, exam locked) is not a
red error card — it is a painted door sign, and meeting the condition flips it. One authored
motion, used only where judgment happens.

## Unresolved

- Product name is still a working name (PRODUCT.md).
- No real question content; placeholder labels stay placeholders.
- Whether admin surfaces should carry the same loud chrome as the learner's or a quieter variant.

## Direction contract

**THESIS.** The interface is the signage of the exam room: flat painted placards, hard painted
keylines, bold condensed caps, and a door sign that reads open or shut. It refuses the pale-blue
SaaS card grid — floating rounded boxes on a near-white ground with one accent — which is
precisely what this prototype is today.

**OWN-WORLD.** The Vietnamese sign triad, flat and painted: **đỏ** `#b31217` (the door, the gate,
the refusal), **xanh** `#0b4ea2` (the learner's own act — every primary action), **vàng** `#f2b705`
(attention, flagged, the mark), on **painted white** `#ffffff` panels over a **wall grey** `#eef0f1`
ground, lettered in **ink** `#14181d`. No gradients, no shadows, no rounded corners: signs are flat
and square, bounded by hand-painted keylines. Bold condensed caps (Bahnschrift) carry every label,
chrome strip and heading; Segoe UI sentence case carries body content; Cascadia Mono carries every
numeral, code and clock. Content sits on the plain white panel the way the counter sits inside the
shop — the sign is loud, the counter is quiet.

**STORY.** A learner understands that the placard in front of them names exactly one condition and
says whether it is met, believes the number shown because it carries its own provenance, and does
the one thing the placard asks.

**FIRST VIEWPORT.** The learner dashboard at 1440. A painted header band in sign-blue: wordmark
left in white condensed caps, the credit placard right on a gold field in dark ink reading
"CÒN 3 LƯỢT THI". Below, the page is a wall of white panels divided by hard painted keylines — not
gaps between floating boxes, but one continuous ruled field. Left rail: navigation as a stack of
painted tabs; the active tab is a solid ink block with white caps. Centre: the weak-part placard,
a red field with white caps naming Part 7, with the distance to the next CEFR band set beneath it
on white. The primary action — sign-blue, solid, square, caps — sits at the head of the centre
column, left-aligned, not centred and not floating.

**FORM.** Vietnamese exam-room and street signage; position 7 of 7 in my grounded list, built
because the roll assigned it; seed key `31ad46c9`.

**FINISH.** unreviewed and undocumented is unfinished; this build ends with the finish review, the
verdict, DESIGN.md, and every shipping raster carrying its provenance.
