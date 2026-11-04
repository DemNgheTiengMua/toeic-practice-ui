# The CEFR rebuild: one pool, one level model, one writer per fact

Synthesized from `docs/SRS.md` (v1.0, 2026-11-04), which consolidates the six research briefs and
supersedes them for implementation. This spec is the repo-aware translation: the SRS does not know the
code, and its vocabulary has to land on tables that already exist. Where the two disagree about what is
already built, this document governs.

**Execution order lives in `docs/research/WORKFLOW.md`.** One epic spec, tickets cut by dependency,
deleted per part as each replacement goes green.

---

## Problem Statement

A learner today is told a level that the system cannot measure, and is shown a plan built on that
number.

Four faults compound, and none can be patched without the others:

1. **The product has 116 items for five certificates and four skills, and none above the third of five
   difficulty levels.** Placement draws from that pool. So a learner who is genuinely B2 is measured
   against items that top out at B1 and is reported as B1 — or, worse, is reported as B2 because a
   certificate band table happened to label a total score "B2".
2. **A band is printed as if it were a CEFR level.** TOEIC 785–940 is labelled "B2" internally and is
   not CEFR B2. The 785 is the sum of two independent per-section minimums; a candidate at 450
   Listening and 335 Reading reaches 785 while remaining below B2 in Reading. Every screen that shows
   that number implies a level it does not measure.
3. **Weakness analysis computes over three populated buckets out of fifteen.** Twelve of the fifteen
   categories hold no items at all, so "your weakest topic" is a statement about an empty set that
   reads as a statement about the learner.
4. **The learning path is a four-week checklist keyed to those unlevelled categories.** A learner at
   B2 listening and A2 speaking receives one blended plan, because the path starts every skill at the
   certificate band. A B2 reader gets A2 reading lessons.

The learner's experience of this is concrete and repeated: they take placement, receive a number whose
meaning nobody defined, find their plan does not change when their ability does, drill practice that
repeats the item they just got wrong, and open weakness analysis to be told their weakest area is one
of three that happen to have content.

For staff, the same root cause appears as tooling that fights the specification: items are authored
against an exam rather than against a skill, level, and topic, so one pool cannot serve five
certificates; rubrics carry a name and a maximum score and no descriptor at any level, so the AI has
been grading against a scale nobody wrote down; and publishing is a status dropdown that three
separate bypasses have already shipped unreviewed content through.

---

## Solution

Five subsystems are replaced rather than extended, and nine that survive are adapted because their
input changes underneath them. The plan a learner experiences:

1. **Content is one pool, addressed by three coordinates.** Every item, task, stimulus and lesson
   belongs to a skill, a CEFR level, and one of twenty topics — never to a certificate. A certificate
   declares which skills it examines and draws from those skills' pools. The pool is addressed by cell
   (`topic × level`; 120 cells across the four skills), and how full any cell is, is content data the
   system reads rather than a number it assumes.
2. **Placement measures, and reports per skill.** A 60-minute, two-stage multistage sitting over four
   skills produces one CEFR level plus a 0–100 position inside it *per skill*, and an overall that is
   the **weakest measured skill** — never an average. The 70% band rule is published on the result
   screen. Reuse within 90 days, a 24-hour cooldown, and progressive results as productive grading
   returns. Free, with no ledger row and no balance check in the code path.
3. **The path is a sliding four-week window, sized by mastery.** Steps point at a coordinate, not at a
   certificate section. Weekly volume is derived from the arithmetic, not asserted. One timeline,
   skewed toward the limiting skill with a floor so no examined skill starves. Progress is reported
   two ways — this week's work, and distance to target — because neither answers the other's question.
4. **Every learner screen says what happened and what to do next.** Nineteen screens replace roughly
   sixty. Three dashboard blocks and no fourth. A level and its position are shown as
   `B2 (78/100) → C1 (12/100)`, framed as advancement, never as a bare percentage. No praise, no
   promises, no raw database key in a DOM node. A hardware gate that refuses to start a sitting with a
   dead microphone or a muted output device, before any attempt row exists.
5. **Staff authoring enforces the specification instead of trusting the author.** Five composable
   permission namespaces instead of job titles, with no-self-approval and read-only triage enforced at
   the transaction layer. The level control is a constraint showing four live gauges, not a dropdown.
   The rubric studio is 28 cells with mandatory descriptors. Publishing is one door behind six live
   invariants. Destructive actions show their blast radius before the button enables.

Underneath all five: **one writer per derived fact**, enforced structurally rather than by convention,
because two writers disagreeing about one row is a confirmed defect in this product and not a
hypothetical.

---

## User Stories

### Levels and measurement

1. As a learner, I want every item I am shown to have a real CEFR level behind it, so that the level I am told means something.
2. As a learner, I want my level reported per skill rather than as one blended number, so that I know which of my skills is holding me back.
3. As a learner, I want my overall level to be my weakest measured skill, so that the headline figure never flatters me into the wrong study plan.
4. As a learner, I want to see which skill is the floor and which is the ceiling, in words, so that I do not have to infer why my overall is what it is.
5. As a learner, I want a position inside my level (`B2 · 71/100`) rather than a bare band, so that I can see progress before I cross a level.
6. As a learner, I want a level crossing shown as advancement (`B2 78 → C1 12`) rather than as a drop, so that improving does not read as collapsing.
7. As a learner, I want my certificate's own score shown as a certificate score, never as a level, so that I always know which of the two numbers I am reading.
8. As a learner on TOEIC, I want to be told plainly that TOEIC does not measure C2, so that I am never shown a level my certificate cannot award.
9. As a learner, I want my certificate score projected from my per-skill position, so that the projection is defensible rather than arithmetically convenient.
10. As a learner, I want a skill I could not be measured on to say "not measured" with a reason and a retry, so that the system does not fabricate a low level for me or silently leave a gap.
11. As a learner, I want never to be shown an average of my skills disguised as my level, so that I am not misled about a weak skill.

### The content pool

12. As a content author, I want to author an item against a skill, a CEFR level, and a topic, so that one item can serve every certificate that examines that skill.
13. As a content author, I want to author a stimulus once with two to four child items, so that a shared passage or audio track is not duplicated per item.
14. As a content author, I want the system to reject a child item more than one level away from its stimulus, so that a 130-word A2 notice cannot be asked a C1 inference question.
15. As a content author, I want the system to reject two children on one passage that test the same text span, so that I am not inflating a learner's score by measuring one sentence twice.
16. As a content author, I want the system to reject audio child items closer than eight seconds apart, so that the learner has time to process one item before the cue for the next.
17. As a content author, I want each item to store an evidence span, so that the review screen can jump to the exact place in the passage or audio that answers it.
18. As a content author, I want to author a four-option single-best-answer item and nothing else for objective skills, so that the 25% guessing floor holds across the whole pool and every derived threshold stays valid.
19. As a content author, I want to tag a stimulus with a domain context (workplace, academic, general) without that tag changing what the item measures, so that register varies while the construct does not.
20. As a content author, I want the system to reject an item whose answer key can be found without reading the stimulus, so that the item tests comprehension rather than world knowledge.
21. As a content author, I want the system to reject an item whose two verifier models disagree about the key, so that a wrong key never mis-places every learner who meets it.
22. As a content author, I want the system to reject a bundle whose siblings have overlapping evidence spans, so that local independence is enforced at ingestion rather than hoped for.
23. As a content author, I want the level control to show me the measured word count, lexical coverage, syntactic complexity, and acoustic rate, so that I select the level the text actually fits rather than the level I intended.
24. As a content author, I want each item to carry a lifecycle state from draft to active, so that nothing reaches a learner before it has passed its gates.
25. As a content author, I want a batch upload to report per-line errors and keep the lines that passed, so that item 247 being wrong does not lose the 246 before it.
26. As a content author, I want the upload format versioned and the item-ID prefix machine-checked, so that a production exam can never bind a demo-prefixed item.
27. As a content author, I want a demo tier and a production tier of the same shape, so that the demo run exercises the real author and the real grader rather than a mock path.
28. As a content author, I want to fill any cell in the matrix and be told which cells are empty, so that coverage scales by depth rather than breadth.
29. As an assessment reviewer, I want a stratified 10% sample of every cell rather than a random one, so that C1 and C2 content cannot be missed entirely by sampling.
30. As an assessment reviewer, I want to solve the sample blind before seeing the AI key, so that my judgement is not anchored by it.
31. As an assessment reviewer, I want one discovered key error to quarantine the whole stratum, so that a systematic defect is not promoted on the strength of a 10% sample.
32. As an assessment reviewer, I want a non-key defect rate above 5% to quarantine the stratum, so that level drift and unnatural distractors are caught without treating every imperfection as a key error.
33. As a content owner, I want an item that has ever left draft to be un-deletable, so that every historical attempt stays renderable exactly as the learner sat it.
34. As a content owner, I want a change to any scored field to require a new version rather than an in-place edit, so that a past attempt never changes under the learner.
35. As a content owner, I want an item with poor discrimination to quarantine itself automatically, so that a bad item stops being served before a human notices it.
36. As a content owner, I want a confirmed-defective item to upgrade the learners who were penalised by it and never to downgrade anyone, so that a content defect is never recorded as evidence about a learner.

### Placement

37. As a learner, I want placement to be free, so that a diagnostic measurement is never gated on a balance.
38. As a learner, I want placement to have no overall countdown, so that closing my laptop for an hour does not cost me the sitting.
39. As a learner, I want to leave a placement and return within 48 hours, so that a 60-minute four-skill sitting fits around a working day.
40. As a learner, I want to return after closing the tab, see which skills are done, which stage I am on, and how many answers are synced, and choose to resume or abandon, so that I am never guessing what state my sitting is in.
41. As a learner, I want a reload to restore the exact item I was on without advancing, so that a refresh is not a free skip.
42. As a learner, I want exactly one recording retry after an interrupted recording, so that a spliced recording is not graded as a worse performance than I gave.
43. As a learner, I want audio playback to resume from the last checkpoint after a reload, so that a page crash does not force me to re-listen from the start of a track.
44. As a learner, I want my answers saved locally while the connection is down, with a visible sync status, so that I can keep working through a dropout.
45. As a learner, I want the flow to pause at a server-dependent boundary rather than pretend to save, so that I never lose a stage transition to a silent failure.
46. As a learner, I want to submit with unanswered items only after confirming a modal that names how many are unanswered in each section, so that I cannot hide the cost from myself and am not blocked for being unable to finish.
47. As a learner, I want my objective results immediately and my productive results as they grade, so that watching the result assemble beats watching a spinner.
48. As a learner, I want a skill whose grading failed to show "not measured" with a retry rather than a zero or an A1, so that a system fault is not recorded as my ability.
49. As a learner, I want a sitting where at least one skill was measured to produce a partial result, so that one bad task does not void an hour of work.
50. As a learner, I want a sitting where nothing was measured to be recorded as abandoned with no result, so that I am not shown a fabricated figure.
51. As a learner, I want my last placement's per-skill results reused for 90 days with their original date, so that changing certificate does not force me to re-measure a skill that has not changed.
52. As a learner, I want to choose between reusing my valid skills and re-measuring everything, so that the choice is mine rather than the system's.
53. As a learner, I want a 24-hour retake cooldown with no lifetime cap, so that a genuine mishap can be recovered from but a lucky draw cannot be farmed.
54. As a learner, I want the cooldown waived when the previous attempt failed on the system's side, so that I am not punished for an outage.
55. As a learner, I want my result reused from a prior sitting to be badged as such, so that I always know how old each number is.
56. As a learner, I want the 70% rule stated on my result in plain language, so that the level is explained rather than asserted.
57. As a learner, I want my speaking and writing tasks in placement capped at 60 seconds and a bounded word count, so that conditions are equal across candidates.
58. As a learner, I want my speaking result bounded by the prompt I was given, so that a fluent answer to a simple prompt does not read as a fluent level.
59. As a learner whose answers were non-monotonic, I want my level resolved to the contiguous run I actually passed rather than flagged at me, so that the instrument stays trustworthy.
60. As an administrator, I want non-monotonic results recorded where I can see them in aggregate, so that a broken pool becomes visible instead of invisible.
61. As a learner on a two-skill certificate, I want placement to ask nothing about the skills my certificate does not examine, so that I am not asked for a microphone on a reading paper.

### The learning path

62. As a learner, I want my plan to be a four-week window rather than a fixed block, so that week six is still about what I can do in week six.
63. As a learner, I want completed weeks to become immutable history, so that my recorded progress never changes behind me.
64. As a learner, I want each skill initialised from its own level and position, so that a B2 reader does not receive A2 reading lessons.
65. As a learner, I want one timeline rather than four parallel strands, so that I am never asked to reconcile four schedules.
66. As a learner, I want my weekly minutes derived from how far I actually am from my target, so that a three-week sprint and a casual year do not receive the same session.
67. As a learner, I want the limiting skill to take the largest share of my week, so that the plan spends my time where it moves my overall.
68. As a learner, I want every examined skill to get at least one step a week, so that the plan does not starve a skill it has decided is fine.
69. As a learner, I want each step to tell me its topic, its level, its item count, and its estimated minutes before I start it, so that I do not discover a twenty-item step after beginning it.
70. As a learner, I want a step to close by being done rather than by improving, so that being genuinely weak does not trap me in one step forever.
71. As a learner, I want a mastered topic's steps to get shorter and never disappear, so that the plan cannot be skipped through free drilling.
72. As a learner, I want a partial step to resume at the exact unsubmitted item, so that I do not re-answer what I already answered.
73. As a learner, I want the answers I gave in a step that expired mid-way to still count toward my mastery, so that closing week one does not erase Friday.
74. As a learner, I want a repeated step to state what I scored and what is needed, so that a repeat reads as a target and not as punishment.
75. As a learner, I want a topic repeated at most once with remedial support, so that I am not looped forever on the thing I cannot yet do.
76. As a learner, I want my plan to change only on one of four named triggers, so that it does not churn after every session.
77. As a learner, I want to be told — and shown the before-and-after numbers — whenever my plan changes, so that a silent rewrite does not destroy my trust in it.
78. As a learner, I want a weak spot to insert a foundational step one level below and pause direct drills for a week, so that I address the cause rather than re-grinding the symptom.
79. As a learner, I want progress reported both as this week's work and as distance to target, so that I can answer both "what do I do today" and "will I make my date".
80. As a learner, I want three concrete options when my target is infeasible — extend the date, raise the hours, lower the target — so that I am not told to keep going by a system that knows better.
81. As a learner, I want a readiness verdict that can say it cannot judge, so that a certificate with no study-hour evidence does not produce an invented number.
82. As a learner, I want a plateau notice after two stagnant cycles with a real choice attached, so that being stuck becomes a decision instead of a slow exit.

### Practice

83. As a learner, I want my path steps and my free drills to use the same item selector, so that practice cannot re-serve the item I just missed while my plan routes around it.
84. As a learner, I want unseen items served before seen ones, and the order deterministic, so that a repeated item is a deliberate choice by the system and not a bug nobody can reproduce.
85. As a learner, I want a wrong item to cool down for 24 hours, so that I am not immediately re-tested on the thing I just demonstrated I cannot do.
86. As a learner, I want a comprehension stimulus I answered correctly to rest for 14 days, so that re-reading the same passage is not counted as comprehension.
87. As a learner, I want a path step never to be blocked by a cooldown, so that the plan does not contradict itself on the day it tells me what to do.
88. As a learner, I want a path step to route around cooling items within its topic and substitute a sibling topic, telling me it did, so that I still practise today.
89. As a learner, I want to drill any examined skill, topic, and level freely, with the three coordinates the plan currently prioritises highlighted, so that revising for my exam is not gated on the plan's opinion.
90. As a learner, I want free drilling to unlock once the week's steps are done, and the lock to name the incomplete steps and their remaining minutes, so that a gate is never a dead end.
91. As a learner, I want a free drill to be a known size — ten items or one productive task — so that I know what I am committing to.
92. As a learner, I want one session per cell per day and a daily overall ceiling, so that I am not exhausting a cell in an afternoon and calling it learning faster.
93. As a learner, I want practice to be free and never to consult a balance or write a ledger row, so that the free flow cannot one day be gated by a wrong number.
94. As a learner, I want mastery estimated over a topic and level rather than over one item, so that repeating one item does not teach me I have mastered a topic.
95. As a learner, I want mastery to be recency-weighted and to require a minimum number of observations, so that one bad early session does not permanently mark me, and four answers do not declare me a master.
96. As a learner, I want a single wrong answer not to drag me out of mastered, so that a slip sets a review window rather than a verdict.
97. As a learner, I want a mastered topic I have not practised for 28 days to decay and revert to developing, so that a stale mastery is re-verified rather than trusted.

### The learner interface

98. As a learner, I want every primary action, active ring, and reached level in one consistent sign blue, so that I never have to learn a second visual language.
99. As a learner, I want warnings, flagged items and unmeasured panels in gold, refusal and failure in red, and open conditions and correctness in green, so that meaning is never carried by colour alone and always paired with a word.
100. As a learner, I want every figure I might compare — a timer, a score, a count, a position — set in tabular monospace, so that columns line up and a number that moves does not jitter.
101. As a learner, I want no rounded corners, no shadows, and no gradients anywhere, so that depth is carried by keyline weight and the product reads as a signboard rather than an app.
102. As a learner, I want selection to invert to a solid ink block rather than tint, so that "selected" is unmistakable at a glance.
103. As a learner, I want a drawn icon beside its word and never in place of it, so that no control is a puzzle.
104. As a learner, I want the dashboard to show exactly three blocks — the next action, my goal standing, and my per-skill standing — so that the one thing I should do today is not buried among ten.
105. As a learner with no placement, I want the dashboard to be one block with one action and a sentence about what placement is, so that I am not shown a grid of empty figures at the moment I have least reason to trust the product.
106. As a learner, I want the next action chosen by a fixed, visible cascade, so that the recommendation is reproducible and not an opinion.
107. As a learner, I want my exam date shown as a date normally and as a gold countdown inside 14 days, so that a twelve-month goal is not under permanent pressure.
108. As a learner, I want the placement result drawn as one signboard with a floor placard above a six-band horizon, so that I cannot average four separate cards in my head.
109. As a learner, I want an unmeasured skill shown as a gold-bordered "not measured" panel with a retry, never as a low level and never in grey, so that a system failure does not read as my result or my fault.
110. As a learner, I want the path drawn as a window of four live weeks with an expandable history, so that I am not shown a total that implies my plan has an end it does not have.
111. As a learner on a two-skill certificate, I want the two missing sidebar doors shown as an explained, non-clickable state, so that navigation does not renumber under me.
112. As a learner, I want to change certificate only in profile settings, so that a one-click switch cannot look like data loss.
113. As a learner, I want an item's CEFR level hidden while I am answering and revealed after I lock it, so that I am not invited to skip hard items and be measured as a beginner.
114. As a learner, I want listening shown as one stimulus group with its children together and reading as a split pane, so that a shared audio track is not split across screens and a passage stays beside its questions.
115. As a learner, I want writing shown as checklist beside editor with a live word counter, so that I can see the requirement while I compose against it.
116. As a learner, I want a resting topic shown dimmed with the local time it reopens, so that it does not look deleted and I do not have to click to find out.
117. As a learner, I want no praise and no promises anywhere in the product, so that every statement is a fact about my work with a next step attached.
118. As a learner, I want every number to carry its unit and its meaning, so that I never have to guess what a figure counts.
119. As a learner, I want no raw database key to reach the page — no `below_A1` in a badge — so that an internal code is never presented as a label.
120. As a learner, I want to be told what the system deliberately does not show me, by its absence being unremarkable, so that suppressed facts never leak as half-explanations.

### Audio equipment and sitting settings

121. As a learner on a speaking certificate, I want the microphone gate before my sitting begins, so that I do not burn sixty seconds of silence and lose a skill to a dead microphone.
122. As a learner on a reading-only paper, I want no microphone permission requested, so that a prompt the sitting will never use does not devalue the next genuine one.
123. As a learner, I want all equipment permission prompts on the settings and pre-sitting screens, never inside a timed sitting, so that no system dialog appears over a running clock.
124. As a learner, I want to see device names only after granting permission, with an explicit control labelled to say what it does, so that I am not shown "Microphone 1" forever with no explanation.
125. As a learner, I want granted, denied, and never-asked shown as three different states with different remedies, so that "denied" is not reported as "no devices found".
126. As a learner whose permission was denied, I want the browser named and the exact control path to re-enable it, plus a retry, so that I am not sent to a generic permissions page.
127. As a learner, I want my chosen input and output device remembered in my browser and never sent to the server, so that the choice survives a reload without being broken on my other machine.
128. As a learner, I want a device list that updates live as a headset connects or a USB interface is unplugged, so that I do not need to reload to change device.
129. As a learner, I want a browser that cannot route audio to a chosen output to say so plainly and use system default, so that I am not offered a selector with no effect.
130. As a learner, I want my chosen output device applied to every audio player in the sitting including review playback, so that a preference that half-works is not a trap.
131. As a learner, I want the microphone test to report signal detected, no signal, or measurement unavailable as three outcomes, so that a browser limitation is not reported as my broken hardware.
132. As a learner with confirmed silence, I want the sitting blocked with a stated-cost override — the speaking skill will very likely be unmeasured and the overall computed from the rest — so that a broken headset does not become a permanently unmeasurable account.
133. As a learner, I want the output test to play a real sound through my chosen device and ask me to confirm, so that a "test passed" badge means something.
134. As a learner, I want one labelled settings control in the sitting topbar carrying type size and contrast alongside audio, so that I do not hunt for two dialogs.
135. As a learner, I want device selection hidden on a reading or writing section and present again on listening or speaking, so that a control appears where it is useful and not where it is noise.
136. As a learner, I want the settings control on placement, practice, and exam sittings alike, so that the longest reading surface in the product also gets a type-size control.
137. As a learner, I want volume in the player beside the audio it controls and not duplicated in the topbar, so that one value does not have two controls.
138. As a learner whose output device disappears mid-playback, I want playback to stop rather than silently escape into the room, and to resume shortly before the interruption, so that I recover what I missed without the audio being public.
139. As a learner, I want recovery to be unlimited while the server clock keeps running, so that a genuinely failing headset is not punished twice.
140. As a learner, I want each recovery recorded against my attempt, so that the pattern is visible rather than invisible.

### Staff interface

141. As a staff member, I want permissions by namespace — authoring, review, blueprint design, support triage, system governance — rather than by job title, so that a small team can combine roles without inventing a new one.
142. As a staff member holding both author and review, I want to be unable to claim, adjudicate, or clear a sample for a batch I uploaded, so that self-approval is structurally impossible.
143. As a support staff member, I want to inspect items, versions, evidence spans and learner attempts and escalate a defect, but not to edit content, keys, rubrics, or a learner's result, so that triage cannot become an unreviewed edit path.
144. As a system owner, I want the no-self-approval and read-only-triage invariants enforced in the database transaction, so that the next endpoint someone writes cannot bypass the controller check.
145. As a content author, I want a coverage matrix showing every cell's quota and deficit, so that "which cell is empty" is a screen rather than a query.
146. As a content author, I want the stimulus-and-children studio to pin the stimulus left and stack children right, lock the child count from the stimulus level, and turn the overlap indicator red above 0.15, so that the two hard shapes are authored correctly and not corrected later.
147. As a content author, I want binding a child to a text span or audio sentence to highlight it in that child's own colour and enforce chronological order, so that evidence spans are authored by pointing rather than by typing timestamps.
148. As a content author, I want the level control to show four live gauges and tell me which band the measured text actually fits when it falls outside the one I selected, so that the level I choose is the one the text demands.
149. As a rubric author, I want twenty-eight cells — four criteria by seven bands — each with a descriptor of at least forty characters, binary gates, and a mandatory quoted-evidence directive, so that the grader has something to grade against.
150. As a rubric author, I want three calibrated anchor responses at bands two, three and four attached to every task, so that grading calls are anchored rather than impressionistic.
151. As a rubric author, I want a seven-response ladder that plots separation between adjacent bands and names the criterion responsible when a segment flatlines, so that a compressed rubric is caught at authoring time instead of in a learner's result.
152. As a rubric author, I want a rubric rejected when adjacent separation falls below 0.65 or the verbose 280-word trap scores above 3.25, so that discrimination is a test rather than a hope.
153. As an exam builder, I want assembly to be a query over the pool with live evaluation of six invariants, so that a paper is built from what the certificate examines rather than from content someone attached to it.
154. As an exam builder, I want every unmet invariant routed to the exact deficit in one click, so that the publish gate guides me instead of blocking me.
155. As an exam builder, I want publishing to be one door behind an audit, with no status field in any request shape, so that the three past status-dropdown bypasses cannot recur.
156. As an exam builder, I want an exam with finished attempts to lock its structure while cosmetic fields stay editable, so that no recorded score can move under a learner.
157. As an exam builder, I want a structural repair to fork a new edition, with mid-sitting learners finishing the edition they started, so that a fix never rewrites history.
158. As an administrator, I want every destructive action to show its blast radius — learners on a path step using this item, learners mid-sitting, historical attempts and how many would be upgraded, published exams pinning it, and the cell's quota impact — before the button enables.
159. As an administrator, I want three confirmation tiers rather than one, so that unlinking a draft item is one click with undo and deactivating a certificate needs a governance role, a drain policy, and a semantic phrase.
160. As an administrator, I want retirement to be the default state change and hard deletion refused for anything that has left draft or has a learner reference, so that history stays renderable.
161. As an administrator, I want retiring a topic coordinate to be prohibited, with staff retiring items within a cell instead, so that the axes of the warehouse cannot be removed.

### Transition

162. As an existing learner, I want my credit ledger preserved exactly, with every historical reason string intact including codes for features that no longer exist, so that my financial record is untouched by a product rebuild.
163. As an existing learner, I want my completed mock attempts preserved and rendered as a historical snapshot with their native score and band, never converted to a CEFR level and never fed into my new state, so that a real sitting remains a real fact.
164. As an existing learner, I want my account, entitlements, and mentor bookings preserved, so that a rebuild is not a reset.
165. As an existing learner, I want my old placement result, path, mastery and insights discarded rather than migrated, so that I am not given a new model wearing old numbers.
166. As an existing learner, I want the answer to "what is my level" to come from placement and the path and not from a two-year-old mock, so that I am not told two different things on the same day.
167. As a system owner, I want the cutover to be one data window with the previous database instance retained as rollback, and code deleted incrementally afterwards, so that a two-path coexistence measured in months cannot happen.
168. As a system owner, I want the rebuild visible behind an account-level flag and not a per-screen or per-user one, so that no individual learner can see two answers during the transition.
169. As a system owner, I want six cross-screen identities to hold after every state change — one per-skill state behind every level, overall equals weakest, one target coordinate behind every distance, mock score always labelled a certificate score, mastery always from the capability record, balance always the ledger sum — so that consistency is checkable rather than asserted.
170. As a system owner, I want the legacy level model to be the last thing switched off, and only when no reader produces a band-derived level and no attempt is mid-sitting on a legacy exam, so that the placeholder is removed on evidence rather than on a schedule.

---

## Implementation Decisions

### D0. The SRS's numbers travel with the spec

The first draft of this spec referenced the SRS instead of carrying it, and an audit found roughly 100
requirements and every numeric table degraded to a sentence. PRN-09 of the SRS forbids exactly that: "it
does not summarize away reasoning or drop options that were rejected." A spec that says "gate 2 checks
linguistic and acoustic profile" is not a specification; it is a pointer, and it is how the thresholds that
define "B1" get invented by whoever implements them.

The tables below are therefore part of this spec, not annexes to it. Where a later decision cites a number,
the number is here.

#### D0.1 The level claim table — what "this item is B1" means

An item at a band must satisfy the lexical, syntactic, cognitive and distractor constraints for that band
simultaneously. An author and a reviewer apply them; gate 2 measures them.

| Band | Lexical profile | Syntactic complexity | Cognitive operation | Distractor construction |
|---|---|---|---|---|
| **A1** | ≥95% tokens within A1 core list (top 800 families); no abstract idioms | MLTU 5.0–7.5 words; simple declarative/interrogative; coordination limited to *and, but, or* | Lexical recognition & verbatim match — locate an isolated word, number, time or set phrase in one clause | Same semantic category (days, prices, places) but absent from the stimulus or explicitly negated |
| **A2** | ≥92% within A1–A2 (top 1,800); concrete everyday and workplace nouns | MLTU 7.5–10.5; basic subordination (*because, when, so, if* Type 0/1); ≤1.3 clauses/T-unit | Local paraphrase & routine transaction — connect a stem by direct synonym to one sentence in a predictable notice, email or short dialogue | One verbatim keyword trap paired with a wrong action or subject; two plausible routine alternatives |
| **B1** | ≥90% within A1–B1 (top 3,000); ≤3% B2 tokens; transparent phrasal verbs | MLTU 10.5–13.5; defining relatives, indirect speech, modal possibility, passive; 1.3–1.6 clauses/T-unit | Cross-sentence synthesis & straightforward pragmatics — combine 2–3 adjacent sentences to identify a main point, intent or direct cause | All three distractors reuse concepts from the stimulus; ≥2 require ruling out temporal reversal or speaker misattribution |
| **B2** | ≥88% within A1–B2 (top 5,000 + AWL 1–5) | MLTU 13.5–17.0; non-defining relatives, participles, mixed conditionals, nominalisation; 1.6–2.0 clauses/T-unit | Bridging inference & discourse stance — infer unstated premises across paragraphs, evaluate pros and cons, detect hedging or disagreement | Partial truths (accurate for one detail, too narrow or broad for the claim), causal inversions, unsupported assumptions |
| **C1** | ≥85% within A1–C1 (AWL 6–10, idiomatic expressions, domain collocations) | MLTU 17.0–21.0; dense hypotaxis, clefts, concessive subordination; >2.0 clauses/T-unit | Pragmatic implication & rhetorical evaluation — decode implied meaning, irony, tone shifts, counter-argument structure | All four options are sophisticated paraphrases; distractors hinge on degree of modality (*inevitable* vs *probable*) or rhetorical scope |
| **C2** | Unrestricted; low-frequency academic, legal, technical, figurative lexis; nuanced register | MLTU >20.0; embedded nominal groups, negative inversion, ellipsis, appositive chains | Abstract critical synthesis & micro-nuance — reconstruct competing implicit frameworks, identify unstated assumptions, distinguish fine stylistic irony | Near-miss analytical interpretations failing only on an implicit condition or secondary entailment |

#### D0.2 The modality envelope — level is modality-specific

An A2 Listening item is not defined by the same textual parameters as an A2 Reading item. Spoken input is
linear, transient and paced by the speaker; written input is static and re-readable. At every band,
listening stimuli carry a **lower syntactic subordination index — roughly 15–20% shorter MLTU** — and a
higher concentration of high-frequency tokens than reading stimuli at the identical band.

| Band | Listening envelope | Reading envelope | Speaking output demand | Writing output demand |
|---|---|---|---|---|
| **A1** | 100–120 wpm; clear articulation; pauses ≥0.8 s between clauses; 40–65 words (20–35 s) | 60–100 words; FK grade 1.5–3.0; notices, signs, 3-sentence messages | 15–30 s; isolated phrases and basic SVO on personal detail | 30–50 words; form completion or 3–5 basic clauses |
| **A2** | 115–130 wpm; minimal elision; 60–95 words (30–45 s) | 120–180 words; FK 3.5–5.5; routine emails, schedules, short memos | 30–45 s; linear clauses linked by *and, then, because* | 60–90 words; short transactional message |
| **B1** | 135–150 wpm; standard contractions; 110–185 words (45–75 s) | 220–300 words; FK 6.0–8.0; multi-paragraph correspondence or informational article | 45–60 s; connected narrative or opinion with reasons | 110–150 words; structured email or short opinion text |
| **B2** | 150–165 wpm; mild reduction; 150–240 words (60–90 s) | 320–450 words; FK 8.5–11.0; complex reports, editorials, multi-viewpoint policy | 60–90 s; systematic evaluation of options, defending a view | 180–250 words; organised essay or formal report |
| **C1** | 165–180 wpm; varied regional accents; implicit prosodic irony; 240–340 words (90–120 s) | 450–600 words; FK 11.5–14.0; dense academic prose, policy critique | 90–120 s in practice; **60 s in placement** | 250–320 words; analytical or persuasive essay |
| **C2** | 175–195 wpm; unscripted debate; dense ellipsis; 260–360 words (90–120 s) | 500–650 words; FK >14.0; abstract theoretical, legal or philosophical text | 90–120 s in practice; **60 s in placement** | 280–350 words; incisive critique or synthesis |

The two recording figures at C1/C2 are a deliberate scope difference, not an inconsistency: placement is
bounded so conditions are equal across candidates; practice may run as long as the learner needs.

#### D0.3 The ±1 stimulus envelope — and its two exceptions

A stimulus imposes a floor and a ceiling on what can be asked of it: a 130-word A2 notice cannot support a
valid C1 inference item, and a 550-word C1 text imposes too heavy a decoding burden for an A2 learner even
when the stem asks for a stated detail.

- When `L_s ∈ {A1, A2}`, **every** child item has `L_i = L_s`. No envelope at the peripheral bands.
- When `L_s ≥ B1`, `L_i ∈ {L_s − 1, L_s, L_s + 1}`.
- **At least two-thirds (≥66%) of children on any stimulus must equal `L_s` exactly.**
- Routing, scoring and mastery computation operate on **`L_i`, never `L_s`.**

#### D0.4 The fourteen question types, with their stimulus and timing constraints

The pool is not "four-option MCQ" in general; it is exactly these types, each with its own bounds.

| Skill | Type | Topics | Stimulus | Stem / option limits | Time |
|---|---|---|---|---|---|
| Listening | `short_exchange` | L-FUNC, L-DET, L-ATT | 20–45 s, 2 speakers, 2–4 turns | stem ≤18 words; options ≤12 | ~45 s |
| Listening | `extended_dialogue` | L-GIST, L-DET, L-INF, L-ATT, L-ORG | 45–120 s, 2–3 speakers, 2–4 children | stem ≤22; options ≤15 | ~50–55 s |
| Listening | `monologue_talk` | L-GIST, L-DET, L-INF, L-ATT, L-ORG | 45–120 s, 1 speaker, 2–4 children | stem ≤22; options ≤15 | ~50–55 s |
| Reading | `sentence_cloze` | R-GRAM | standalone 15–45 words, one blank, 1 item | options 1–5 words | 30–40 s |
| Reading | `passage_comprehension` | R-GIST, R-DET, R-INF, R-ATT | 60–650 words, 2–4 children | stem ≤25; options ≤18 | ~60 s |
| Reading | `text_insertion_cohesion` | R-COH | 120–650 words, four numbered markers | four options = four positions | 55–65 s |
| Speaking | `descriptive_monologue` | S-DESC | prompt 30–60 words + optional image | 3 bulleted sub-prompts | prep 30–45 s; record 45–60 s |
| Speaking | `situational_negotiation` | S-INT | scenario 50–90 words + 3 options | justify one, argue against two | prep 45–60 s; record 60–90 s |
| Speaking | `argumentative_monologue` | S-ARG | proposition 40–70 words + 3 points | sustained argument + counter | prep 60 s; record 90–120 s |
| Speaking | `visual_data_synthesis` | S-SYN | table or chart + query 40–70 words | synthesise, resolve conflict, recommend | prep 45 s; record 60–90 s |
| Writing | `transactional_correspondence` | W-CORR | prompt 60–100 words, 3 required actions | 50–150 words by band | 8–15 min |
| Writing | `visual_data_synthesis` | W-DATA | chart/table/process + prompt 40–70 words | 60–180 words | 10–20 min |
| Writing | `persuasive_essay` | W-ARG | thesis or policy claim 40–80 words | 80–300 words by band | 14–30 min |
| Writing | `analytical_evaluation_essay` | W-DISC | multi-perspective prompt 50–90 words | 80–320 words by band | 14–30 min |

`ItemType` is a column on `Items`, not a derived property: each row above validates and renders differently.
Certificate-specific variants that would change the 25% guessing floor — IELTS True/False/Not Given, TOEIC
Part 2's three-option question-response — are authored as four-option cognitive equivalents: statement
verification under `R-DET`, conversational speech-act matching under `L-FUNC`.

#### D0.5 Cell depth and the volume tiers

The pool is sized **by cell**, not by exam. A cell is one `(topic, cefr_level)` intersection — 120 per skill
matrix across the four skills (36 listening, 36 reading, 24 speaking, 24 writing; **120 is the total, not
per skill**).

| Band group | Objective items per cell | Productive tasks per cell |
|---|---|---|
| Core (A2, B1, B2, C1) | 30 | 8 |
| Peripheral (A1, C2) | 15 | 4 |

**Why 30.** One mastery verification window needs up to 15 items. A weak learner's cycle is an initial
5-item diagnostic, a post-lesson 5-item drill, a spaced 5-item review and a 10–15 item verification window —
25 to 30 unseen items. Thirty supports two full verification windows, or six 5-item drills, without
repeating an item.

**Why 8 tasks.** Generalisability Theory needs 3–4 tasks for a dependable productive judgement. Eight per
cell allows a practice round, AI feedback, a lesson and re-verification with no prompt repetition.

**No cell may ship empty.** Coverage scales by depth, never by breadth — the rule whose absence produced
twelve empty topics.

The full production pool is **2,120 assessment units plus 120 lessons**:

| Skill | A1 | A2 | B1 | B2 | C1 | C2 | Total |
|---|---|---|---|---|---|---|---|
| Listening items | 90 | 180 | 180 | 180 | 180 | 90 | **900** |
| Reading items | 90 | 180 | 180 | 180 | 180 | 90 | **900** |
| Speaking tasks | 16 | 32 | 32 | 32 | 32 | 16 | **160** |
| Writing tasks | 16 | 32 | 32 | 32 | 32 | 16 | **160** |
| **All skills** | **212** | **424** | **424** | **424** | **424** | **212** | **2,120** |

Stimulus counts: 330 shared listening audios, 232 shared reading passages, 150 standalone `R-GRAM` cloze
stimuli (382 reading stimuli total).

| Tier | Objective items | Productive tasks | Lessons | Units | Empty cells |
|---|---|---|---|---|---|
| Legacy (replaced) | ~116 total | included | 13, none levelled | 116 | 12 of 15 |
| **Tier 1 — seeded demo** | 180 L + 180 R (110 audios, 114 stimuli) | 48 S + 48 W | 120 | **456** | 0 |
| **Tier 2 — MVR** | 480 L + 480 R (176 audios, 204 stimuli) | 80 S + 80 W | 120 | **1,120** | 0 |
| Tier 3 — full pool | 900 L + 900 R (330 audios, 382 stimuli) | 160 S + 160 W | 120 | **2,120** | 0 |

Neither tier may ship a partially populated cell matrix: a demo learner must be able to sit a complete
two-stage placement and run a full 5-item drill in any of the 120 cells.

**120 lessons**, one per cell, indexed `(skill, topic, cefr_level)`. Each carries two sub-modules:
`module_primary` (the rule, a level-calibrated strategy, two annotated worked examples, 600–900 words) and
`module_remedial` (distractor anatomy for objective topics, or a band k−1 → k upgrade comparison for
productive topics, plus two guided micro-checks, 400–600 words). The split exists so a second attempt at the
same cell does not re-read identical instruction. A lesson therefore carries its own `CefrLevel`.

#### D0.6 The rubric: criteria, scale, composite, and the anti-compression mechanisms

One unified four-criterion analytic rubric per skill, scored on **integers 0–6 where the integer is the CEFR
band**: 0 = Pre-A1/non-scorable, 1 = A1, … 6 = C2.

| Skill | Criteria |
|---|---|
| Speaking | `S_FC` fluency/coherence/discourse · `S_PR` pronunciation/intonation/intelligibility · `S_LR` lexical resource/pragmatic precision · `S_GR` grammatical range/real-time accuracy |
| Writing | `W_TR` task fulfilment/relevance/argument · `W_CC` coherence/cohesion/organisation · `W_LR` lexical resource/register · `W_GR` grammatical range/complexity/accuracy |

The task composite is the unweighted mean: `M_t = ¼ Σ c_j ∈ [0, 6]`, mapped by uniform half-band cuts:
`M < 0.75` Pre-A1; `[0.75, 1.50)` A1; `[1.50, 2.50)` A2; `[2.50, 3.50)` B1; `[3.50, 4.50)` B2;
`[4.50, 5.50)` C1; `≥5.50` C2. A two-task writing sitting weights the extended task twice:
`M_skill = (M_T1 + 2·M_T2) / 3`.

**Descriptors are floor gates, not adjectives.** Every criterion carries a written descriptor at every one
of the seven bands, stating observable thresholds — speech rate in wpm, pause duration, error-free T-unit
percentage, subordination variety, lexical repetition. A descriptor that is merely "at least 40 characters"
satisfies the schema and grades nothing, which is the defect this requirement exists to kill.

**Five mandatory anti-compression mechanisms.** Direct holistic prompting collapses scores into B1/B2 and
rewards verbosity:

1. Every band transition is a set of **binary gate conditions**, not a scalar impression.
2. The four criteria are evaluated in **four separate calls at temperature 0.20**, each extracting verbatim
   evidence before emitting an integer.
3. **Six acoustic telemetry metrics** are injected into the speaking prompt alongside the transcript:
   speech rate, articulation rate, silent pauses over 1500 ms per minute, mean pause duration, ASR
   confidence mean, phoneme posterior score. Without these, `S_FC` and `S_PR` become transcript judgements.
4. Hard length and repetition ceilings: `W_TR`/`S_FC` capped at **2** when a response falls below 50% of the
   task minimum; `W_LR`/`W_CC` capped at **3** when type-token ratio or n-gram repetition reveals formulaic
   padding.
5. **Three calibrated benchmark anchors** at bands 2, 3 and 4 are injected into every grading call.

**Dependability, and what each task count permits.** One AI-scored task is not a defensible measure of a
skill.

| Tasks | Φ | SEM (bands) | Permitted decision |
|---|---|---|---|
| 1 | 0.550 | ±0.67 | Formative feedback only; **never sets a level** |
| 2 | 0.710 | ±0.47 | **Placement level assignment** (two distinct genres) |
| 3 | 0.786 | ±0.39 | Topic mastery at peripheral bands |
| 4 | 0.830 | ±0.34 | Skill promotion and core topic mastery |
| 6 | 0.880 | ±0.27 | Certificate readiness benchmark |

Rater agreement with expert consensus is high (QWK ≈ 0.72–0.82 writing, 0.70–0.78 speaking), but score
generalisability across the skill domain at one task is only `Φ(1) = 0.55`. Where a task fails to grade the
skill is neither zero nor A1: one graded task yields a result marked as estimated from one task; zero
graded tasks yields "not measured".

The prompt ceiling is part of the composite: `M_t` is **bounded at `k_task + 1.00`**, so a task scored on an
A2 prompt cannot yield a C1 result however well it is answered. Otherwise a fluent learner answering a
simple prompt receives a fluent answer's score, which measures the prompt, not the learner.

#### D0.7 Item lifecycle, quarantine thresholds, and the monotonic-protection rule

Six states: `draft → validated → active`, plus `quarantined`, `retired`, `retired_defective`. Selection
queries filter on `active` **and** `content_tier`; review queries join on `(item_id, version)` with **no
lifecycle filter**, so a past attempt always renders exactly what the learner sat.

**Telemetry quarantine.** Every active objective item is evaluated when it crosses **30, 50, 100 and 250**
administrations. Four conditions flip it to `quarantined`, removing it from selection immediately:

1. Point-biserial discrimination below **0.10** at N ≥ 50, or below **0.00** at N ≥ 30.
2. Target-band facility outside **`[0.30, 0.92]`** at N ≥ 50.
3. One distractor chosen by more than **45%** of target-band learners while the key falls below **35%**.
4. A productive task's mean AI score deviating more than **0.75 bands** from the learners' established level
   at N ≥ 30.

**Monotonic learner protection.** When a quarantined item is confirmed defective, every historical attempt
referencing it is recalculated. If the recalculation lifts a band or promotes a topic from non-mastered to
mastered, the learner is upgraded automatically. **No learner is ever downgraded because of a content
defect** — a system error is not evidence about a learner.

#### D0.8 Non-monotonic placement results truncate; they are never silently accepted

Band L is awarded only at ≥70% correct at L **and at every evaluated lower band**. Correct at B2 but wrong at
A2 means guessing or a bad item, so the awarded level truncates to the **highest band of the first contiguous
passing run from the bottom**, and the anomaly is recorded with the band that failed and the items that
caused it.

A boolean `IsNonMonotonic` is not enough: without which band failed, "a high rate of these means the pool is
broken" cannot be detected. The admin surface therefore reads an anomaly table, not a flag count.

#### D0.9 The intra-band position, and the three worked examples

For objective skills:
`P = round(100 × [0.70 × (clamp(p_L, 0.70, 1.00) − 0.70)/0.30 + 0.30 × clamp(p_{L+1}, 0, 0.70)/0.70])` —
70% weight on excess mastery above the threshold at the awarded band, 30% on progress toward the threshold at
the next band. At Pre-A1, `P = round(100 × p_A1 / 0.70)`. At C2, only the first term applies.

For productive skills, `P_prod = round(100 × (M_skill − B_low(L)) / (B_high(L) − B_low(L)))`, where
`B_low`/`B_high` are the half-band cut intervals of D0.6.

**Worked behaviour, asserted at seam 2:**

- 5/7 on B2 (0.714) and 1/7 on C1 (0.143) → **B2·9**.
- 7/7 on B2 and 4/7 on C1 (0.571) → **B2·94**.
- One more C1 item correct (5/7) crosses to **C1·3**.

The number tracks the boundary continuously and never shows a bare percentage.

#### D0.10 The waiting constants, collected

| Constant | Value | Where it applies |
|---|---|---|
| Placement reuse window | 90 days | PLC-06; stale badge from day 60 |
| Retake cooldown | 24 hours, no lifetime cap | PLC-07; zero if every measured skill failed to grade |
| Placement inactivity expiry | `min(last_activity + 48h, started_at + 72h)` | PLC-13 |
| Second-reload recording threshold | finalise if ≥15 s captured, else replace with unseen sibling, no retries | PLC-15 |
| Item cooldown, wrong answer | 24 hours | PLC-59 |
| Item cooldown, correct discrete grammar | 72 hours | PLC-59 |
| Item cooldown, correct comprehension | 14 days | PLC-59 |
| Daily ceiling | one session per cell per day; 50–60 objective items or 4 productive tasks overall | PLC-64 |
| Mastery thresholds | `τ_low = 0.733`, `τ_high = 0.80`, `n_min` = 12 items or 3 tasks | PLC-68 |
| Mastery inactivity decay | 0.02 per week after 28 days unpractised | PLC-71 |
| Weakest-skill skew | `γ = 1.5`, floor `d_s = 0.15` | PLC-53 |
| Weekly budget floor | `max(1.5, H_required)`; steps packed to budget ±10 minutes | PLC-51 |
| Span overlap ceiling | intersection-over-union < 0.15 | CNT-12 |
| Audio inter-item gap | ≥8.0 seconds | CNT-10 |
| Free drill size | 10 objective items or 1 productive task | PLC-63 |
| Exam-date countdown | gold inside 14 days | LRN-15 |
| BORDERLINE feasibility | remaining ≤15 hours and every skill within 0.25 level | PLC-79 |
| Stagnation | level movement ≤0.05 across lagging skills despite ≥50% step completion | PLC-81 |
| Path abandonment | four weeks of inactivity | PLC-82 |
| Gate 3 verifier confidence | key ≥0.85, each distractor <0.15, ablation reject at ≥0.50 | CNT-37 |
| Gate 2 audio WER | 0.0% | CNT-36 |
| Rubric discrimination | adjacent separation ≥0.65; verbose trap ≤3.25; composites strictly monotonic | ADM-10 |

### D1. The level model is one type, and it is not a difficulty scale

A single `CefrLevel` value type (Pre-A1 as a reportable floor; A1–C2 as authored levels) is introduced in
`BusinessObjects` and used by entities, services, contracts and views. There is no second 1–5 scale and no
mapping between them.

**`Questions.DifficultyLevel` is deleted, not migrated.** It carries a `CHECK BETWEEN 1 AND 5` and is
referenced in **148 places across `src/` and `tests/`** — 7 entities/wire DTOs, 4 admin services, 4 import
paths, and 30+ test files. This is the single most expensive mechanical change in the rebuild and it is
sequenced first because every later decision reads the level. Every seed, fixture, import format and test
that writes `DifficultyLevel` must write `CefrLevel` instead.

### D2. The pool is decoupled from the exam by inverting the ownership

Today `Questions` → `QuestionGroups` → `Exams`, so an item cannot exist without an exam. The SRS forbids
that (PRN-05), and no amount of service-layer work fixes it: the foreign key is the bug.

The schema change:

- `QuestionGroups` becomes **`Stimuli`**: pool-owned, keyless to any exam, carrying the passage or audio,
  the stimulus's own `CefrLevel`, domain context, transcript and prompt copy.
- `Questions` becomes **`Items`**: pool-owned, carrying `SkillId`, `TopicId`, `CefrLevel`,
  `PlacementEligible`, `ContentTier` and `LifecycleStatus`. `ExamId` and `SectionId` leave the table.
- **`ItemVersions`** is new and holds everything scored: stem, four options, key, explanation, evidence
  span, reference text/kind. Copy-on-write; cosmetic explanation typos are the only in-place edit.
- **`ExamItems`** is new and is the assembly binding — `(ExamId, SectionId, CertId, ItemVersionId,
  DisplayOrder, UnitNumber)`. This is where the composite `CertId` isolation now lives, and where
  `INV-6` (a production exam binds no demo-prefixed item) is checked.
- `AttemptAnswers` and `PracticeAnswers` pin **`ItemVersionId`**, not `QuestionId`, so a past attempt
  renders exactly what the learner sat. `AttemptAnswers.ExamId` stays — it is the containment key the
  isolation triggers need.

Consequence to plan for: `AttemptEngine` reads questions through group→section→skill today. Its
`GetAttemptTakeAsync` projection has to be rebuilt against `ExamItems` joined to `ItemVersions`. This is
one seam, in one service, and it is the reason placement can reuse the engine (D9).

### D3. `QuestionCategories` becomes `Topics`, cert-agnostic and 20 rows

`QuestionCategories` is `(CertId, Code, Name)` — a per-certificate, unlevelled label. It is replaced by
**`Topics(TopicId, Modality, Code, Name)`** with exactly 20 rows: 6 listening, 6 reading, 4 speaking, 4
writing. No `CertId`.

Every reader of `CategoryId` moves to `TopicId`, and the set is small but wide: `Questions`,
`Lessons`, `UserCategoryMasteries`, `PathModuleTopics`, `Recommendations`, plus
`vw_UserCategoryAccuracy` and `vw_UserSectionAccuracyAll`.

`vw_UserCategoryAccuracy` is replaced by **`vw_UserTopicAccuracy`**, grouping by `(TopicId, CefrLevel)`.
This is the deterministic SQL the weakness analysis and the AI insight both read; neither computes its
own accuracy.

### D4. Mastery is keyed to the cell

`UserCategoryMasteries` becomes **`UserCellMasteries(UserId, TopicId, CefrLevel, …)`**, with no `CertId`
— mastery follows the topic, so a learner who switches certificate keeps a comparable record (D-19).

The Beta-Binomial arithmetic (`p_m = (α₀ + Σwᵢcᵢ)/(α₀+β₀+Σwᵢnᵢ)`, `wᵢ = 0.85^(K−i)`, `α₀ = β₀ = 2.0`,
`τ_low = 0.733`, `τ_high = 0.80`, `n_min` = 12 items or 3 tasks) lands in `MasteryMachine` as a pure
function, extending the shape that already exists.

The prior mastery decision's rule survives unchanged and is now a hard requirement of the SRS (PLC-70): **state
changes only on an event, never by recomputation.** Those are `answer_correct`, `answer_wrong`,
`wrong_again_while_mastered` (lapse), `maintenance_pass`, and `inactivity_tick` (0.02/week after 28 days).
A recomputed-state design makes "two consecutive wrong answers" unreachable, which is exactly the class
of defect this rebuild exists to remove.

### D5. Placement produces per-skill rows, written by one writer

**New table `PlacementSkillResults`**: `(PlacementId, SkillId, ResultStatus, CefrLevel,
PositionInBand 0–100, ContinuousCoordinate, ProjectedBandCode, ProjectedScore, CutScoreTableVersion,
IsEstimatedFromOneTask, IsNonMonotonic, FailureReason, TopicPriorsJson, CriterionProfileJson,
MeasuredAt, ExpiresAt, ReusedFromPrior)`.

`ResultStatus` is an enumeration, not a boolean: `measured`, `estimated_from_one_task`, `grading`,
`not_measured`, `exempt_accommodated`. The SRS distinguishes all five on screen (PLC-19, PLC-33, PLC-35,
LRN-18) and a `CefrLevel` column that is sometimes null cannot.

`FailureReason` carries the three-code vocabulary the UI's "not measured with a reason and a retry" and
the zero-cooldown rule both read: `candidate_skipped`, `inactivity_timeout`, `ai_grading_failed`.

`TopicPriorsJson` stores, per topic touched (`1–3` items), `{topic_id, evaluated_band, items_seen,
items_correct, routing_signal}` where signal is `weak` or `on_level`. This is placement's only
contribution to path initialisation, and it is a **sequencing hint, never mastery** — three items cannot
support a mastery judgement, which needs twelve.

`CriterionProfileJson` stores `{c1..c4, weakest_criterion, priority_fix}` from the productive rubrics, so
the path has a weakest-criterion starting point without recomputing it.

**`PlacementTests` changes shape.** `ResultBandCode`, `TotalScore` and `CK_Placement_DoneHasResult` are
the exact constraint that made the confirmed defect a live crash
(`docs/research/placement-free-response-defect.md`). They are replaced by:

- `IsPartialResult`, `CompletionFailureReason`, and `LimitingSkillId`, and
- **`done ⇒ (at least one measured `PlacementSkillResults` row) OR a `CompletionFailureReason` is
  recorded`**, enforced by a trigger rather than by a band-code CHECK.

**`Overall` is not stored.** It is `MIN` over the certificate's examined skills at read time (D15), and
`LimitingSkillId` is the tie-broken name of the skill that produced it — tied by **lowest position** when
two skills share a level. Storing the overall as a column would be a second writer of one fact, and would
let a stored value disagree with `MIN`.

**Single writer (PRN-02, TRN-06).** The placement snapshot writer is the only code allowed to write
`PlacementTests` status and the `PlacementSkillResults` rows, and it derives them from
`ExamAttempts` + `AiGradings` after grading settles. `PlacementService` stops writing that row — the
current second write is the defect. A pending productive task leaves the placement `in_progress`, not
`done` with a null result, and the UI says "still grading" rather than forcing the row.

**Reload during grading is a pure read** (PLC-16). It never re-dispatches the grading job and never resets
a task's status; a re-dispatch is how one free attempt becomes a paid duplicate.

`PlacementService` remains a thin adapter over `IAttemptEngine` (PLC-10): it owns reuse, cooldown,
target-band and free-flow rules, and owns no sitting state machine.

### D6. The certificate's scale gains per-skill cut scores; `ScoreBands` survives

`ScoreBands(CertId, Code, MinTotal, MaxTotal)` cannot express "TOEIC Reading ≥385 is B2 while 785 total
is not". That is why the current product's B2 label is wrong, and removing the label without adding the
table leaves projection with nowhere to read from.

**New table `CefrCutScores(CertId, SkillId, CefrLevel, MinScaled, MaxScaled, TableVersion)`** — the
§4.6 per-skill cut-score table, read-only at the presentation layer, versioned so a recorded projection
can be reproduced (LVL-13). Projection is per-skill from the continuous coordinate, then aggregated by
that certificate's own rule (sum two sections for TOEIC L&R; average four and round to 0.5 for IELTS and
VSTEP; no combined total for TOEIC S&W).

`ScoreBands` stays for what it genuinely is — the certificate's own band rows — because a mock exam
keeps its native band (TRN-04, D-26). Placement no longer writes into it.

`CefrCutScores` rows for a certificate that cannot express a level (TOEIC tops at C1, VSTEP has no C2)
are the display-ceiling rows; a C2 row records the highest the scale can show and is never emitted as a
level.

### D7. The equalized paper: a fixed routing stage, then a boundary stage

This is the reconciliation of "how many items a sitting uses" against "how many the pool holds". It is
the most numerically dense part of the product and the part most likely to be built from the prose and
get the numbers wrong, so every figure below is a test at seam 2.

**Stage 1 is a fixed blueprint, 8 items per objective skill, sampling all six bands.**

| A1 | A2 | B1 | B2 | C1 | C2 |
|---|---|---|---|---|---|
| 1 | 1 | 2 | 2 | 1 | 1 |

Stage 1 spans A1 and C2 deliberately: a blueprint that omits the ends cannot route a beginner or an
advanced candidate, and the two-stage design becomes a design for the middle only.

**Stage 2 is the boundary stage. The routing is a pure function of the Stage 1 score, and every routed
band ends with exactly 7 evaluated items** (Stage 1 contribution + Stage 2 delivery).

| Stage 1 score | Routed pair | Stage 2 delivery | Evaluated per band |
|---|---|---|---|
| 0–2 | A1–A2 | 6 A1 + 6 A2 | 7 / 7 |
| 3 | A2–B1 | 6 A2 + 5 B1 | 7 / 7 |
| 4–5 | B1–B2 | 5 B1 + 5 B2 | 7 / 7 |
| 6 | B2–C1 | 5 B2 + 6 C1 | 7 / 7 |
| 7–8 | C1–C2 | 6 C1 + 6 C2 | 7 / 7 |

**Why 7.** Converting "≥70% at band L and every evaluated lower band" into discrete counts is what makes
the passing bar land off the cutoff. At n=5 the bar is 4/5 = 80%, giving 26.27% false demotion of true
masters; at n=6 it is 5/6 = 83.3%, spiking false demotion to 34.46%; at n=7 it is 5/7 = 71.43%, sitting
on the 70% cutoff with 9.63% false promotion, 14.80% false demotion, and 1.29% pure-guess false passes.
Seven is the count at which the stated rule is not systematically unfair to competent candidates.

**No substitution ever crosses a band.** If a cell is exhausted the selector relaxes the topic constraint
first, then draws from the practice reserve at the same band, then recycles the oldest-seen item at that
band and logs a warning. Substituting an adjacent band would change the denominator and invalidate the
70% rule — so it is a refusal, not a fallback.

**Partial stimulus delivery is required, not optional.** Stage 1 asks for 1 item at a band and Stage 2 for
5–6, while knowing stimuli carry 3–4 children and `R-GRAM` cloze carries 1. Neither the 1-item nor the
6-item draw can be assembled from intact same-band child groups, so the engine must serve *k of M*
children from a shared stimulus. This is a hard requirement on the sitting projection, not an
optimisation: without it a 5-item maintenance check cannot be built from 3-item audio groups either.

**The productive half draws at the routed lower band.** Task 1's anchor is `min(level_L, level_R)` derived
from the objective half, never a band chosen before the objective result exists. A two-skill certificate
with no objective result anchors Task 1 at B1 and routes Task 2 from Task 1's grade, falling back to the
learner's declared starting band if grading exceeds three seconds.

**Placement writing prompts are scoped below their practice envelope.** Practice allows 250–350 words at
C1/C2 (14–30 min per task), which two of them cannot fit inside the 22-minute placement writing budget.
Placement-eligible C1/C2 prompts are therefore scoped to 120–140 words (Task 1) and 180–220 words
(Task 2), and placement speaking is capped at 60 seconds for every band.

### D8. Placement stock is an occupancy problem, and the launch tier does not solve it

Six non-overlapping forms is the settled count. Each form needs 7 placement-eligible items per band per
objective skill; the pooled sitting consumes at most 7 per band, so the demand is **42 per band** (7 items
× 6 forms, or 7 per topic cell across 6 topics).

Reconciling that demand against supply is where the launch tiers fail. The eligible ratio is **one third of
cell depth** — 10 of 30 in a core cell, 5 of 15 in a peripheral cell (CNT-24) — which is the figure the
per-tier numbers below are derived from:

| Tier | Eligible/band, A1 & C2 | Eligible/band, A2–C1 | Six forms need 42/band | Verdict |
|---|---|---|---|---|
| Full pool | 30 | 60 | 252 eligible total | **Short 12 at A1/C2** |
| **MVR (launch gate)** | **20** | **30** | 252 | **Short 92** |
| **Demo (phase-3 gate)** | **10** | **10** | 252 | **"1.4 forms"** |

The full-pool column keeps the SRS's 2:1 core-to-peripheral ratio (60 eligible in an A2–C1 band, 30 in an
A1/C2 band). MVR and Demo shrink both bands to the same eligible count, which is why they fail harder at
A2–C1 than at the periphery.

`CNT-25`'s remedy — re-tag 12 existing practice items per peripheral band as placement-eligible — closes
the **full-pool** gap only. At MVR and Demo, placement-eligible stock cannot supply six non-overlapping
forms, and the SRS's own phase 3 ships placement on demo-tier content. **The spec therefore treats this as
an unresolved blocker, not an implementation detail.** CNT-25 closes two doors by name — it is "not by
authoring new items, and not by reducing the number of forms" — so the remaining options are to **raise the
MVR tier**, or to **widen phase 3's content gate to MVR** before placement goes live. Phase 3 shipping
objective-only placement with productive skills marked unmeasured is rejected outright by the SRS
(Transition OQ-TR-04) as "a partial result that looks complete".

The same deficit appears from the practice side, and it is the deeper one: `PLC-68` needs 12 observations to
master a cell and `CNT-32` needs 3 tasks for peripheral productive mastery, while an even MVR spread gives a
core cell ~16 items and a peripheral productive cell ~2 tasks. **MVR is declared the launch gate but cannot
run the mastery model it launches.**

**Decision required before phase 3.** The spec does not pick between raising MVR and widening phase 3's
gate; it records that the choice is unavoidable, and that the assumption to test is whether MVR is
distributed evenly across cells at all — the SRS publishes tier totals but never the Tier-2 per-cell
distribution, so the figures above are the even-spread reading of the published totals.

### D9. `IAttemptEngine` gains a staged-sitting vocabulary; there is no second engine

`IAttemptEngine` is extended, not duplicated: a stage index, the routed band pair for stage 2, per-band
evaluated counts, partial-stimulus delivery, and the no-overall-countdown expiry
(`min(last_activity + 48h, started_at + 72h)`), replacing the single absolute `ExpiresAt` computation for
placement purposes. Mock exams keep their own absolute expiry under the same engine.

`AttemptEngine` stays the sole owner of answer validation, deadline enforcement, autosubmission,
two-step scaling (`ScoreScales`) and band matching (`ScoreBands`). `PlacementService` and
`MockExamService` stay thin adapters over it.

**Free placement is a data fact, not a check** (PRN-06): placement, practice, path steps, free drills,
lessons and weakness analysis call no credit method at all. Nothing is added "just in case".

### D10. The path's step is a coordinate, and templates stop defining steps

`StepCoordinate = (SkillId, TopicId, TargetCefr, StepType, SizingTier)` becomes the step's definition.
`LearnerPathSteps` gains those columns and loses `PathModuleItemId` as the thing that says what a step
is.

**`PathTemplates`, `PathModules`, `PathModuleItems`, `PathModuleTopics` are dropped.** The prior decision kept them as
"scaffolding" because a step had to belong to a module. The SRS removes that requirement: steps are
synthesised by `LearningPathPlanner` from current capability, and a step that needs a template is a step
that cannot be re-planned. Dropping them also removes `LearnerPaths.TemplateId` and the whole triple of
composite keys that existed only to pin steps to templates.

**The window.** `LearnerPaths` gains `WindowStartDate` and the active week index; `LearnerPathSteps`
gains `WeekSequence` for absolute ordering plus a `closed_week` marker. Completed weeks become immutable
rows — `WindowNumber` alone is not enough, because week 1 next month is a different week.

**Exactly four weeks are live at any moment.** Week 1 closes when all its steps are done **or the calendar
crosses day 8**. Week 2 then becomes week 1, and a new week N+4 is synthesised from current capability.
Completed weeks move to history and are immutable. A week pointer with no close rule either never slides or
slides on the wrong event.

**Step lifecycle:** `locked → available → in_progress → closed`, sequentially by order within the active
week. A partially finished step resumes at the exact unsubmitted item. If the week expires mid-step,
answers already given still count toward capability and **unattempted items return to the pool** — the
re-queue is what keeps the selector's cooldown and `times_seen` arithmetic correct after an expiry.

**The step-type bindings.** `StepType` is an enumeration with three distinct execution paths, not a label:

| Step type | Binds to | Executes through |
|---|---|---|
| `diagnostic_drill`, `focus_practice`, `review_spaced` | `(skill, topic, level)` + a count | the unified item selector (D11) |
| `concept_lesson`, and `expanded_remedial` with an embedded lesson | a leveled `lesson_id`, using `module_primary`, plus `module_remedial` when remedial | the lesson reader |
| `mock_section` | `(skill, level)` + a section | `IAttemptEngine` |

**Sizing follows mastery** (`SizingTier` has exactly these three values):

| Tier | Items / tasks | Minutes |
|---|---|---|
| `mastered` | 5 items or 1 short task | ~6–8 |
| `developing` / unmeasured | 8–10 items, or 1 full task, or lesson + 4 items | ~12–15 |
| `low` | lesson (primary + remedial) + 12–15 items, or 2 tasks | ~20–25 |

A mastered topic's steps shorten; they are never removed.

**Level advance is gated on three conditions together.** A step moves to `level + 1` only when the topic is
mastered at the current level **and** the skill's position is **≥ 75** **and** the certificate's target band
lies above the current level. Otherwise the step stays on-level, with periodic **5-item maintenance checks**
so a mastered topic does not decay unnoticed.

**Ordering is pedagogical.** Within a skill and topic: lesson → blocked practice → interleaved review.
Across the timeline, consecutive steps alternate skills. Reversing this imposes undesirable difficulty on a
beginner before declarative knowledge exists.

**Surplus and deficit hours.** With more hours than the pool can supply, **up to 30%** of surplus minutes
move to productive work and cross-topic review, the substitution cascade applies, and the week is capped
with a visible notice rather than silently repeating items. With too few hours, the plan packs strictly
within the hours available, compresses mastered topics to 5-item checks, and surfaces the feasibility
verdict with three concrete options: extend the date, raise the hours, or lower the target.

**Re-planning: four triggers, and only four** (PLC-73):

1. Two consecutive weeks with zero completed steps.
2. Any examined skill's level changing — from a mock section, a placement retake, or calibration.
3. The learner editing hours, date, or target.
4. A weak-spot escalation.

**Weekly automatic re-planning is prohibited.** A plan that changes after every session stops being
trusted; a plan that never changes drifts uncorrected.

On a re-plan, **history is immutable** — completed weeks, closed steps, and in-progress steps with saved
answers. **Superseded and regenerated** — unstarted steps in the active week and all buffered steps, from
current capability and the refreshed feasibility calculation. Every re-plan writes an audit row and shows a
dismissal-capable summary card naming the trigger, the before-and-after step counts **and sizes**, and the
refreshed verdict.

**Weak-spot escalation has a shape:** direct drills on the topic pause for one week; a foundational bridge
step one level below is inserted; a diagnostic check is scheduled into week 2.

**Terminal states.** Four, and only two of them are flows: target reached and exam date arrived are the
primary flows with full detail; stagnated and abandoned are a **badge plus one sentence**, without a
separate flow. Stagnation is two consecutive four-week cycles, or three weak-spot escalations, with level
movement **≤ 0.05** across lagging skills despite **≥ 50%** step completion; the plateau notice offers two
options: consolidate one sub-level lower, or retake placement. Abandonment is **four weeks of inactivity**,
resumable from saved capability at any time.

**The readiness verdict has four outcomes**, reusing the feasibility arithmetic: `READY`; `BORDERLINE`
(remaining **≤ 15 hours** and every skill within **0.25 level**); `NOT_YET_READY`; and `CANNOT_JUDGE` where a
certificate's bands carry no study-hour evidence. `NOT_YET_READY` produces a concrete continuation, not
encouragement: the per-skill deficit, the hours the limiting skill still requires, and the minimum feasible
new exam date computed from the weekly hours available.

`LearningPathPlanner` gains the weekly-budget arithmetic (`H_required = ΣH_s(current→target) /
max(1, days/7)`, `T_weekly = 60 × min(target, max(1.5, H_required))`) and the skew
(`W_s = d_s^1.5 / Σd_j^1.5`, `d_s = max(0.15, θ_target − θ_current)`) as pure functions, replacing the
step-count-then-minute-divide approach of the previous plan.

### D11. One practice selector, so path and free drilling cannot disagree

Two selectors exist and they already disagree — `SmartPracticePlanner` for free drilling and
`LearningPathService.Steps` selecting by `PathModuleTopics.DisplayOrder` for path steps. `docs/STATUS.md`
§2 names this as an open problem. The rebuild collapses them: one selector, ordered by
`times_seen ASC, last_seen_at ASC, item_id ASC`, filtered to `(cert, skill, topic, level,
placement_eligible = false)`, honouring the item cooldowns (24h wrong, 72h discrete grammar, 14 days
comprehension) and never blocking a step (PLC-60 substitutes a sibling topic and says so).

The path's `DisplayOrder` selection is deleted. This removes the last caller of `PathModuleTopics`, which
D10 already drops.

### D12. Daily ceiling is a view, not a counter

The daily volume ceiling (one session per cell per day, 50–60 objective items or 4 productive tasks per
day) is derived, per NFI-05: a **`vw_DailyPracticeVolume`** returning per-learner, per-day answer counts
and distinct cells. No stored counter to drift.

### D13. The rubric's shape changes, and descriptors become mandatory

`GradingCriteria` today is `(Code, Name, RubricDescription NULL, MaxScore DECIMAL(5,2), Weight)`. The
SRS fixes an analytic rubric of four criteria per skill, scored on integers 0–6 where the integer *is*
the CEFR band, with a written descriptor at every one of the seven bands.

- `RubricDescription` becomes **NOT NULL**, and the seven-band descriptor set moves into a new
  **`GradingCriterionDescriptors(CriterionId, Band 0–6, Descriptor, BinaryGates)`** table — one row per
  cell of the 28-cell grid, which is what makes "descriptor at every band" checkable rather than
  editorial.
- `MaxScore` becomes fixed at 6 for authoring purposes; `SectionCriteria.MaxScore` survives because a
  TOEIC Speaking section genuinely scores 0–3 where another scores 0–5 on the same criteria.
- Anchors: **`GradingCriterionAnchors(CriterionId, Band, ResponseText)`**, three per task at bands 2, 3, 4.
- The descriptor rule survives: a criterion with no descriptor is not graded, it is `Failed` with a reason and a logged
  criterion code. `Weight` continues to multiply into the aggregate.

The composite stays `M_t = ¼ Σ c_j` with the half-band cuts of CNT-29, and the two-task writing weight
`(M_T1 + 2·M_T2)/3`.

### D14. Single-writer is enforced by structure, not convention

Three structural assertions on every derived-fact table, per TRN-07:

1. A **required writer-identifier column** (`WrittenByService`).
2. A **trigger** that rejects a write whose declared writer is not the table's registered owner.
3. A **monotonic `ProjectionVersion`** so an older out-of-order callback cannot overwrite a newer result.

Owners are registered in a new **`DerivedFactOwners`** lookup: placement result, learner per-skill
state, attempt score and band, topic capability, step closure, window synthesis, credit movement.
Seven facts, seven owners.

This is what makes the confirmed defect class detectable at write time instead of at read time.

### D15. Learner per-skill state is its own table and its own writer

**New `LearnerSkillStates(UserId, CertId, SkillId, CefrLevel, PositionInBand, ContinuousCoordinate,
MeasuredAt, ExpiresAt, Source)`** — written only by the skill-state writer. Every screen that answers
"what is my level" reads this, so TRN-13's first identity is a single row read rather than a
recomputation per page.

`Overall` is not stored: it is `MIN` over measured skills of the certificate's examined vector, computed
at read. Storing it would be a second writer of one fact.

### D16. Five composable permission namespaces replace job titles

`Roles` survives (it is referenced by `Users` and by the auth pipeline). New
**`Permissions(Code)`** with the five namespaces as these exact codes, and
**`RolePermissions(RoleId, PermissionCode)`**: `pool.content.author`, `pool.content.review`,
`assessment.blueprint.design`, `operations.support.triage`, `system.governance.admin`. Checks move from
role-code string comparisons to permission codes.

Two invariants are enforced in the transaction layer, not in controllers (ADM-04):

- **No self-approval.** An account cannot claim, adjudicate or clear a review sample for any batch it
  uploaded **or last modified** — the "or last modified" clause is what stops re-upload-then-approve.
- **Read-only triage.** Triage may inspect items, versions, evidence spans and learner attempts and may
  escalate a defect, but may not edit item text, keys, rubrics, **exam structure**, or override a learner's
  result.

**`can_override ⊆ {}`.** Neither invariant can be bypassed by any combination of the five namespaces: an
account holding all five still fails both checks. That is the escape hatch ADM-02/03 exist to close, and a
trigger that merely reads "does the acting user hold permission X" reopens it.

The trigger scope must cover every table a prohibited write can reach — including `ExamItems` and the section
tables, not only review samples and item edits. A check on item edits alone leaves exam structure editable by
triage, which ADM-03 forbids in the same sentence.

### D17. Authoring gates are server-side, and the four gates are one seam

Gate 1 (structural hygiene), gate 2 (linguistic and acoustic profile), gate 3 (blind dual-model key
verification and stimulus ablation), gate 4 (level consistency, span independence, seven-response ladder
for productive tasks) each produce a rejection reason and an audit row. They live behind one ingestion
service seam so a rejected bundle is refused once, before any row is written, and the reason is
per-line.

Each gate's thresholds are the tables in D0.1–D0.9, not a summary:

1. **Gate 1 — structural and item hygiene.** Four distinct options with pairwise Levenshtein distance >
   2; no meta-options ("all of the above", "neither a nor b"); the key's word count within ±25% of the
   distractor mean; part-of-speech parallelism across options; **key balance between 20% and 30% per letter
   within each cell**; no unique content lemma appearing in both stem and key but absent from the stimulus
   and distractors; non-empty explanation and per-option distractor rationale.
2. **Gate 2 — linguistic and acoustic.** Word count, FK grade, MLTU, clauses per T-unit and target-band
   lexical coverage inside D0.1–D0.2. For audio, an independent ASR pass (Whisper-Large-v3) against the
   written script must yield **word error rate 0.0%** — a single mispronounced or clipped word breaks every
   learner who reads the transcript in review.
3. **Gate 3 — blind dual-model key verification.** Two heterogeneous verifier models, both distinct from
   the author, each solving with the key and explanation withheld: both must select the exact key with
   confidence **≥ 0.85** and assign **< 0.15** to every distractor. A stimulus-blind ablation follows: if a
   solver identifies the key at **≥ 0.50** without the passage or transcript, the item is rejected for
   testing world knowledge rather than comprehension.
4. **Gate 4 — level consistency and grader calibration.** An independent classifier must predict
   `L̂_i = L_i`. Sibling evidence spans satisfy IoU < 0.15, **except when one item is a whole-passage
   `R-GIST` question**. For productive tasks, a seven-response synthetic ladder — six benchmark responses
   from A1 to C2 plus an adversarial "verbose B1 trap" of 280 words of repetitive simple clauses — must
   produce **strictly monotonic** composites with `ΔM ≥ 0.65` between adjacent bands and a trap composite
   **≤ 3.25**.

**Three model families, strictly:** `Author_LLM ≠ Verifier_LLM ≠ Grader_LLM`. The same family authoring an
item and later validating its key shares its own blind spots; the same family grading responses it authored
inflates scores for its own phrasing. Both failures are systemic, not occasional. The blueprint dispatch that
starts an authoring run carries the exact word-count, FK grade, MLTU and lexical bounds for one empty cell,
and audio synthesis targets the band's wpm from D0.2.

**Promotion scope.** An approved stratum promotes the 10% reviewed **and the 90% unsampled buffer together**.
The 90% is never individually reviewed: that is the accepted risk of skipping a pilot study, and it is why
the sample is stratified rather than random. An implementer who promotes only what was reviewed stalls the
pool at 10% of target volume.

The single-writer and lifecycle rules already named in `AGENTS.md` §1.1 and by repo decision D28 stay in
force: no hard delete, `HasTrigger` on every triggered table, version bump for any scored field — **including
a stimulus, its audio file, or its level**. Versioning items alone means editing a shared passage silently
rewrites every past attempt that sat it.

### D18. The audio gate is client-attested and recorded server-side

`ExamAttempts.PreflightVerifiedAt` already exists and stays. The gate applies **identically to placement,
mock exams and speaking practice** — one gate, not three, because three drift and that is exactly what
LRN-54 was written against.

- The gate is scoped to the skills the certificate actually examines, and refuses **before any attempt
  row exists** (PLC-19a). A reading-and-writing certificate is gated on nothing — requesting a microphone
  for a reading paper asks for a permission the sitting will never use.
- **Permission is obtained before the sitting, never during it.** It is raised on profile settings and the
  pre-sitting screen, both of which the learner reaches with intent. A system dialog appearing over a
  running clock is an interruption the learner cannot control.
- **Device names require permission, and that is a browser rule rather than a defect.** The settings
  surface offers an explicit control labelled to say what it does ("Enable the microphone to see device
  names"); the listing refreshes once granted.
- **Permission state is read from the browser, not inferred** from a failed `getUserMedia`: `granted`,
  `denied`, and unasked are three states with different copy and different remedies.
- **Naming and availability are different failures.** Granted + empty list → "connect a device" (a device
  they must plug in). Labels withheld → "enable the microphone" (a browser setting they must change).
  Collapsing both into "no devices found" is the state the product ships today.
- **A denied permission is explained in place**, naming the browser and the exact control path, with a
  retry.
- **The microphone test measures signal, not permission**, and reports one of three outcomes: signal
  detected, no signal detected, or **measurement unavailable**. Only the middle outcome is evidence about
  the learner's hardware; the third is evidence about the browser and is not a failure.
- **No signal blocks by default, with a stated-cost override.** The learner is told why; the confirmation
  path states the consequence in advance — the speaking skill will very likely be reported as not
  measured, and the overall computed from the remaining skills. A hard refusal converts a broken headset
  into a permanently unmeasurable account.
- **The output test plays a real sound** through the chosen device at the learner's chosen volume and
  requires confirmation. A "test passed" badge with no audible sample proves nothing about a muted device.
- **Device choice persists in the browser only.** Identifiers are salted per origin and per browser
  profile; storing them server-side would break routing on any other machine.
- **The list is live.** Devices appearing and disappearing update without a reload; the device in use is
  marked, and a vanished device is **not silently replaced**.
- **A browser without output-device routing degrades explicitly**: the surface says so and states that
  system default will be used, rather than offering a selector with no effect.
- **The chosen output applies to every audio player in the sitting**, including review playback.
- **One settings control in the topbar**, present on placement, practice and exam sittings alike, opened
  by a labelled control and never an icon alone. It carries font size and contrast alongside audio. The
  **audio portion is hidden on sections that use no audio** and returns on the next listening or speaking
  section; font size and contrast remain available throughout. Suppression is per **section**, never per
  item.
- **Volume is not duplicated.** It belongs to the player beside the audio it controls; the topbar carries
  no second volume control.
- **A device lost mid-sitting pauses and offers recovery.** Playback stops rather than silently rerouting
  to system speakers — in a scored listening section, audio escaping into the room is worse than a pause.
  The settings control opens, the learner selects a replacement, and playback resumes shortly before the
  interruption.
- **Recovery is unlimited and the clock does not stop.** Three properties bound the damage: the settings
  control is reachable at any moment so an interruption is not needed to open it; the clock is
  server-authoritative and never pauses for a device event; and every recovery is recorded.
- **`AttemptEquipmentEvents(AttemptId, Kind, At)`** records each recovery so the pattern is reviewable.
- The verdict is a **client attestation** (LRN-54a). The server records it and cannot verify it; that is
  the accepted risk, and the alternative — a server pretending to observe a browser permission — is a
  lie in the schema.

### D19. Display registry replaces every raw code path to a DOM node

One registry maps every internal code (band, level, topic, step type, status, mastery state) to display
text, with an exhaustive mapping and a safe fallback that logs. `below_A1` in a badge is a known defect
in this product (LRN-34); a test asserts no unmapped code can render.

The design system rules are absolutes, not defaults: **`border-radius: 0`, `box-shadow: none`, no
gradients, no backdrop blur, anywhere.** Flat painted fields and keylines — **2px ink** for panel and sign
frames, **1px soft rule** for internal dividers. Depth is carried by keyline weight, never by shadow.
Active and selected items **invert to solid ink with white text**; they never tint. Ink is both lettering
and field. Colour is always the second layer of information, paired with a word or a number.

Three type voices: a reading face for sustained prose, a condensed signage face for uppercase placards and
labels, and a monospace for **every** figure. A number set in the reading face is a defect. The family is
**IBM Plex, self-hosted with Latin and Vietnamese subsets**, so diacritics render from the same family
rather than falling through to a fallback. **No emoji and no raw unicode glyphs** — icons come from a drawn
set, sit beside a word, and never replace it.

Desktop-first at 1440×900 with deliberate density: the answer grid, the passage, the clock and the save
status share one screen, because a 200-item exam under a clock demands it. A requirement that genuinely
cannot be met inside these rules is **stated and argued**; using a rounded control or a soft shadow is not
a workaround, it is answering a different product.

### D19a. The screen inventories

Nineteen learner-facing screens replace roughly sixty fragmented views. The API and view work is scoped by
this list; a screen not on it is not in phase 3.

| ID | Screen | Purpose |
|---|---|---|
| `SCR-SH-01` | Global shell and topbar | Six sidebar doors, active certificate placard, exam-date indicator, offline banner |
| `SCR-DB-01` | Dashboard | One next action, weekly execution, per-skill standing |
| `SCR-PL-01` | Placement preflight and reuse gate | What will be measured, what may be reused, hardware gate |
| `SCR-PL-02` | Placement sitting — objective | Two-stage listening and reading |
| `SCR-PL-03` | Inter-half checkpoint and productive sitting | Objective result, then speaking and writing tasks |
| `SCR-PL-04` | Placement result | Four levels plus overall, weakest-skill floor, progressive grading |
| `SCR-LP-01` | Path timeline | Four-week window, dual progress, re-plan card |
| `SCR-LP-02` | Goal setup and feasibility | Target, date, weekly hours, verdict |
| `SCR-LP-03` | Terminal readiness verdict | Ready, borderline, not-ready with continuation |
| `SCR-LS-01` | Leveled lesson | Primary and remedial modules, micro-checks |
| `SCR-PRAC-01` | Objective practice hub | Topic matrix, mastery, next recommended drill |
| `SCR-PRAC-02` | Writing practice hub | Task launch, prior submissions |
| `SCR-PRAC-03` | Speaking practice hub | Task launch, prior recordings |
| `SCR-PRAC-04` | Objective practice session | Item, answer, post-answer review |
| `SCR-PRAC-05` | Productive practice session | Prompt, record or compose, AI feedback |
| `SCR-EX-01` | Mock exam catalogue | Available mocks per certificate |
| `SCR-EX-02` | Mock exam sitting | Timed paper, navigator, autosave |
| `SCR-EX-03` | Mock exam result | Certificate score, projected level, per-section split |
| `SCR-PR-01` | Profile and certificate switcher | The only place a certificate changes |

**Six sidebar doors: Path, Exams, Practice, Writing, Speaking, Home.** Writing and Speaking are their own
doors because learners could not find productive practice when it was buried inside a generic "practice"
entry. A two-skill certificate keeps all six and shows the missing ones as a non-clickable, explained
state — removing them renumbers the navigation and breaks muscle memory.

Certificate switching lives in profile settings; **the topbar placard is not clickable.** Switching changes
the path, the placement and the practice pool, so a one-click switch invites an accidental change that
reads as data loss.

Ten staff screens replace ten controllers and thirty-one loosely coupled views:

| ID | Screen |
|---|---|
| `S-01` | Warehouse coverage and blueprint readiness matrix (120 cells, quotas, deficits) |
| `S-02` | Bulk JSON ingestion and staging workbench |
| `S-03` | 10% stratified audit and quarantine queue |
| `S-04` | Author remediation and rejection workbench |
| `S-05` | Master-detail stimulus and child item studio |
| `S-06` | Acoustic waveform and alignment dock |
| `S-07` | Productive task and visual stimulus studio |
| `S-08` | Analytical rubric and grader calibration studio |
| `S-09` | Certificate blueprint and exam assembly workbench |
| `S-10` | Consequence impact, telemetry and defect triage console |

`S-07` matters more than its row suggests: it is the only screen that authors the writing prompt and
speaking task that every rubric anchor attaches to. Without it the productive half of the pool cannot be
authored at all.

### D20. Exam assembly, the six publish invariants, and editions

**Assembly is a query over the pool, not an ownership relation.** The six invariants are evaluated live
while editing, on a short debounce, and each unmet invariant routes in one click to the exact deficit. The
gate **guides rather than blocks** — but it is not optional: making it optional is how three bypasses shipped
unreviewed content through status dropdowns.

| Invariant | Rule |
|---|---|
| **INV-1** | Every enabled skill has at least one section; every section declares at least one item and has exactly that many active items bound |
| **INV-2** | Every shared stimulus bound to an exam carries its complete child group, satisfying the envelope, the span-independence rule, and the audio gap rule |
| **INV-3** | Every bound item is active, has verifier agreement, and has cleared its stratum's human audit |
| **INV-4** | Every enabled productive skill has a calibrated, locked rubric and every bound task carries three anchors |
| **INV-5** | Section timers sum to the declared exam duration, and the cut-score projection is monotonic and non-overlapping |
| **INV-6** | A production exam binds no demo-prefixed item |

INV-5 is the one that makes a paper unsittable when it is wrong: timers that overspend the declared duration
produce a sitting the learner cannot finish.

**Publishing is one door:** a single action after an audit, never a status field. `Status` is removed from
every request shape. A created exam is always a draft, and import and generation cannot set it.

**An edition is a first-class concept.** An exam with finished attempts locks its structure — no adding,
removing or reordering items, no key edits, no question-type changes. Cosmetic fields stay editable because
they cannot change a recorded score. A structural repair **forks a new edition**; learners mid-sitting
finish the edition they started, and **every historical attempt stays pinned to the edition it sat**.

Pinning `ItemVersionId` alone does not achieve this: it keeps the *items* renderable, not the paper, its
timers or its cut scores. `ExamEditions(ExamId, EditionNumber, Status, PublishedAt)` plus an `EditionId` on
`ExamItems` and on `ExamAttempts` is what makes "the edition it sat" a fact rather than a hope.

**Three confirmation tiers**, not one, scaled to consequence:

1. Structural unlink on a draft — a single click with an undo window.
2. Warehouse retirement or a defective key — **a reason code, a replacement decision for pinned mocks, and
   typing the exact item identifier**. This is the tier that touches the most dangerous operation: retiring
   an item that published mocks pin. Without the reason and the replacement decision, live exams break
   silently.
3. Certificate deactivation or tier decommission — a governance role, an explicit learner drain policy, and
   a semantic phrase.

**Retirement is the default state change**, because it removes an item from future assembly while keeping
every historical attempt renderable. **Retiring a topic coordinate is prohibited** — topics are the axes of
the warehouse; staff retire items within a cell, never the cell.

### D21. The nine surviving readers are adapted, never replaced

Every one of them reads a band or a level whose meaning changes underneath it, so every one is adapted; none
is replaced.

| Reader | After |
|---|---|
| Mock exam scoring | Keeps the certificate score; **adds** a projected CEFR level as a separate, labelled quantity. The two are never merged into one figure |
| Exam review | Binds each item to its `(item_id, version)`; shows the CEFR level only after submission |
| AI insight | Receives a structured payload: four per-skill coordinates, overall, limiting skill, target, feasibility. Narrates only; never computes |
| Weakness analysis | Aggregates by topic and level, because flat aggregation conflates stimulus difficulty with cognitive mastery |
| Goal and feasibility | Uses a continuous CEFR hours function; returns `cannot_judge` where evidence is absent |
| Calibration | Compares only accuracy **at the learner's own level**, and proposes — never applies — a change |
| Credit ledger | Unchanged in financial structure. Placement, path steps, free drills, lessons and failed-AI retries remain free |
| Mentor booking | Sends a structured multi-skill standing payload, and says "does not examine this skill" where the certificate does not |
| Saved questions | Links to `(item_id, version)`, renders the pinned version even when retired, and flags a retired-defective item |

`GoalFeasibilityCalculator` and `LearningPathPlanner` keep the no-proration rule: a band with no study-hour evidence
returns `cannot_judge`, never a prorated estimate.

### D21a. The dashboard, the result signboard, and the path window

**The dashboard has exactly three blocks and cannot become four.** It was cut from eleven blocks to three
because eleven hid the one action that mattered; a rebuild that grows it back is a regression regardless of
what the blocks contain.

1. **Next action and weekly execution** — one primary call to action, a discrete step tally, minutes done
   against allocated.
2. **Goal standing and feasibility** — **four tabular cells, no progress bars**: current level per skill
   against target, remaining guided hours, pacing comparison, verdict.
3. **Per-skill standing** — one row per examined skill, sized by certificate configuration: **four columns
   for a four-skill certificate, two for a two-skill certificate. Never a four-column grid with two empty
   cells.**

**The next action is chosen by a fixed six-step cascade**, not by an opinion, so the choice is reproducible:
(1) resume an in-progress placement; (2) retry an unmeasured skill; (3) the week's next available step;
(4) the recommended free drill; (5) the day's ceiling state; (6) completion.

**With no placement, the dashboard is one block with one action** and a sentence describing what placement
is and **how long it takes**. No ghost blocks, no dashes. A new learner seeing a grid of empty figures reads
a broken product, and this is the only moment they have no reason to trust it.

**The result is one unified signboard, not four disconnected numbers**: an overall floor placard, tethered to
the limiting skill, above a six-band horizon table in which every skill is a row on one shared ordinate.
Four separate cards invite the learner to average them mentally, which is exactly what the overall exists to
prevent. The overall is **labelled as the floor, with its reason in words**, naming the ceiling skill and the
floor skill.

An unmeasured skill is a **gold-bordered** panel reading "not measured", with a short reason and a retry
control — never a low band, and never grey. Grey reads as the learner's fault; the bottom band is a
fabricated number; gold is attention, not refusal.

**The 0–100 position is shown as a position, never a percentage.** Crossing a band resets it downward, so the
presentation carries the crossing explicitly: **`B2 (78/100) → C1 (12/100)` in Sign Green**, framed as
advancement. A bare "12%" after "78%" reads as collapse.

The result carries a **provenance strip**: the published 70% rule in plain language, an **"AI-estimated"**
label beside productive scores, **each skill's measurement date**, and a reuse badge where a result was
carried over. **Exactly one primary call to action** ends placement — several equal actions is no action.

**A stale result is marked.** A result reused up to 90 days carries its measurement date; **from 60 days it
gains a gold age badge and an action to re-measure that skill.**

**The path is shown as a window, not a plan with an end:** a five-tab week selector with an expandable
history drawer for completed weeks and **three visible future weeks**. No "9 of 20"; the position is a
window, and the UI says so in words rather than implying a total.

**A step shows its size before it starts**, in four metadata columns: sequence and skill, the canonical topic
code and name, the level badge, and the exact item count with estimated minutes. **A repeated step carries a
label and the numbers** — what was scored last time and what is needed. A bare "repeat" reads as punishment.

**A resting topic is shown, dimmed, with the local time it reopens** — the learner's local time, never UTC.
Hiding it makes it look deleted; revealing it only on click is a dead end.

**Free drilling's locked state shows why**, naming the incomplete steps that unlock it and how many minutes
the remainder should take. A lock with no explanation is the most hostile state in the product.

**An item's CEFR level is not shown while the learner is answering.** Showing it invites avoiding hard items,
which corrupts the measurement: a learner who skips B2 items is measured as A2. It may be revealed after the
answer is locked, when it cannot affect behaviour.

**The three practice modalities are three different interfaces**, not one wrapper. Listening shows one
stimulus group with its linked children at a time, because a shared audio track cannot be split across
screens. Reading shows a split pane: passage left, items right. Writing shows a split pane: checklist left,
editor right with a live word counter. Speaking shows **one task at a time with a preparation timer**.

**Copy rules, which are requirements and not style notes:** no praise and no congratulation, because this is
a signboard and not a coach; no promises, and every trajectory statement is a **conditional derived from the
feasibility arithmetic with its inputs visible**; every state says what happened and what to do next, never
how to feel about it; every number carries its unit and its meaning — `B2 · 71/100 in band, 14 of 20 correct
(70%), ~315 guided hours remaining, written 184 of 150–220 words`.

**Seven facts are suppressed deliberately:** item CEFR level during answering; the stage-2 routing band;
the non-monotonic anomaly flag; grading-failure internals (stack traces, phoneme posteriors, temperature);
results for skills the certificate does not examine; mastery probability as a decimal; and raw target
coordinates as coordinates. A suppression list is a negative requirement, and each of these will ship as a
leak if it is not named.

### D21b. v1 accessibility hooks carried in the data model

Four non-breaking hooks are present so enabling them later is not a rewrite: an examined-skills vector
stored per path rather than four hardcoded cards; a per-skill `exempt_accommodated` status so a listening
skill can be excluded from the overall without failing the sitting — carried on the **skill result row**, not
only on learner state; a `TimerMultiplier` of 1.0 / 1.5 / 2.0 on timed components; and a spelling-exemption
flag decoupling orthographic error from the grammatical and lexical criteria. None is exposed in v1 UI.

### D22. What survives unchanged and must not be touched

`CreditTransactions` and the append-only trigger (D9 of the SRS decision log rejects a second mutation
guard). The credit ledger's historical reason codes, including codes for deleted features. `Users`,
entitlements, `MentorProfiles`, `Bookings`. `ScoreBands` as the certificate's own band rows.
`ScoreScales` as the certificate's own two-step conversion. The composite-`CertId` isolation pattern in
`Sections`, `Exams`, attempts and practice — `ExamItems` extends it rather than replacing it.

The ledger's three structural rules survive with it: **no stored balance column** (balance is always
`SUM(Amount)`), **no debit without its source row owned by the same account**, and **no movement may drive a
balance negative**.

### D23. Transition: what is preserved, what is dropped

The rule that decides preservation: **preserve what is an independent fact; drop what is a derived opinion.**

| Data | Disposition |
|---|---|
| Credit ledger | **Preserve as-is**; exported and imported with every historical reason string intact |
| Users and entitlements | **Preserve and augment** |
| Certificate catalogue and band tables | **Preserve and augment** — gains examined-skill vectors, per-skill cut scores, per-skill target coordinates |
| Completed mock attempts | **Preserve**, tagged `legacy_v1`, read-only |
| In-progress and placement attempts | **Drop** |
| Placement results | **Drop and start clean** |
| Topic mastery | **Drop** |
| Learning paths, templates, modules, steps | **Drop and regenerate** |
| Insights | **Drop**, except those attached to preserved mock attempts |
| Legacy items, topics, lessons, rubrics | **Drop and replace** |
| Saved questions | **Drop legacy links**; start clean against the warehouse |
| Mentor bookings | **Preserve**; refresh the payload |

**Preserved mock attempts carry an epoch tag and are read-only.** They never feed learner state, never
underwent a fake conversion, and never render alongside a placement level **without a distinct header**. The
learner's question "what is my level" is answered by placement and the path, not by a two-year-old mock.

**The cutover is a single window for the data, not for the code**: fresh schema, import of preserved data,
and the flag flipped once, inside a stated window, with the previous database instance retained as rollback.
Code is deleted incrementally afterwards, as replacements verify. A two-path coexistence measured in months
is the failure mode; a two-hour data cutover is not.

**Six consistency identities must hold across every screen after every state change:**

1. Every surface answering "your level" reads the same per-skill state and returns identical level and
   position.
2. Wherever an overall appears, it equals the weakest measured skill.
3. Every distance figure derives from the same target coordinate.
4. A mock exam's score is always labelled a certificate score, **never a level**.
5. Every mastery figure comes from the capability record, not from a recomputed percentage.
6. Every balance equals the ledger sum.

**A step ships on evidence beyond green tests:** a concurrency pass with simultaneous submissions and
out-of-order grading callbacks; a psychometric pass proving every active item passed blind dual-model
solving and stimulus ablation; a copy-sanitisation pass proving no raw database key reaches a DOM node; and
a **financial pass reconciling every ledger balance against a pre-cutover snapshot**. The financial pass is
the only thing that catches a cutover which silently dropped credits.

**Two smoke journeys are mandatory**: a learner who has never taken a placement, and a learner mid-path.
Either failing means the step did not ship.

### D24. Sequencing, and the gate on each step

Per TRN-09, ordered by content availability first and consistency second:

| Phase | Ships | Gate to start the next |
|---|---|---|
| 1 | Level model, pool schema, topics, gates 1–4, authoring screens | Zero items required; the pipeline refuses a bad item before any content exists |
| 2 | Authored pool, 10% audit, calibrated rubric versions | Demo tier (456 units + 120 lessons) with no empty cell |
| 3 | Placement, path, adapted readers, learner screens, behind an account-level flag | Demo-tier content; demo accounts only |
| 4 | Cutover: fresh schema, preserved data imported, flag flipped | MVR tier (1,120 units); preserved ledger and mock attempts verified by checksum |
| 5 | Exam re-publication and pool expansion | Six invariants green per exam; telemetry at N = 30 |
| 6 | Removal of legacy paths, the flag and the shims | Every exit criterion met |

The deletion rule from `docs/STATUS.md` §2 holds and is not negotiable: **delete each old flow part by
part as its replacement goes green, never in one sweep at the end.** The last thing switched off is the
legacy level model itself, and only when no reader produces a band-derived level, no attempt is mid-sitting
on a legacy exam, and the six consistency identities hold across all accounts and all five certificates.

The deletion rule from `docs/STATUS.md` §2 holds and is not negotiable: **delete each old flow part by
part as its replacement goes green, never in one sweep at the end.** The last thing switched off is the
legacy level model itself, and only when no reader produces a band-derived level, no attempt is mid-sitting
on a legacy exam, and the six consistency identities hold across all accounts and all five certificates.

---

## Testing Decisions

**What makes a good test here.** It drives the real thing against a real database and asserts an
observable outcome — a row that must exist, a refused write, a returned level, a rendered string. It does
not re-implement the code under test. The repo has been bitten by exactly that: the first #146 regression
test copied the endpoint's `catch` into the test body and could never fail. Every test below asserts a
fact the product would have to actually produce.

**Seam 1 — `tests/Services.Tests` against a real SQL Server** loaded fresh from
`docs/database-schema.sql` (triggers and views included) via `DatabaseFixture`. This is the default seam
for any behaviour that writes a derived fact, because a derived fact is defined by its constraints and
its single writer.
*Prior art:* `PlacementServiceTests`, `PracticeMasteryTests`, `DynamicPathGenerationTests`,
`PathStepCompletionTests`, `CreditLedgerTests`, `PlacementFreeResponseProbeTests`.
*What lands here:* placement snapshot single-writer (including the exact free-response case that crashes
today), per-skill result production with all five `ResultStatus` values, reuse and cooldown, mastery
transitions by event, the one selector, window synthesis and step closure, the six INV-1..6 publish
invariants, the no-self-approval invariant, the daily ceiling view, the catalogue of readers adapted in D21,
and **the refusal case for non-overlapping forms**: a sitting drawn from a pool that cannot supply six forms
must fail loudly rather than repeat an item inside one administration.

**Seam 2 — pure static functions in `BusinessObjects`.** Every load-bearing number in the SRS is
arithmetic, and arithmetic deserves a pure function with no database in it.
*Prior art:* `LearningPathPlannerTests`, `MasteryMachineTests`, `GoalFeasibilityCalculatorTests`,
`LegacyAggregationRuleTests`.
*What lands here:* every worked example and every closed-form figure in D0 — the `B2·9` / `B2·94` / `C1·3`
positions (D0.9), the 165-minute allocation (PLC-54), the false-promotion/false-demotion table at n=5, 6 and
7 including the **8.70% / 26.27% / 34.46% / 9.63% / 14.80% / 1.29%** figures and the `5/7 = 71.43%` bar, the
stage-1→stage-2 routing table (D7, all five rows), the half-band cuts (D0.6), the weekly budget and skew
(D10), the Beta-Binomial (D4), the objective and productive position formulas (D0.9), the prompt ceiling
(D0.6), the mastery decay (PLC-71), and the BORDERLINE thresholds (15 hours / 0.25 level).
The SRS wrote these outcomes down so they could be asserted, and a test that reproduces them is a test
that cannot silently drift. Any of them changing without the others being recomputed is the class of change
this seam exists to catch.

**Seam 3 — `docs/schema-tests/domain*.sql` through `run-all.sh`.** Every rule the SRS says the database
must refuse.
*Prior art:* `domain15` (multi-cert scoring), `domain19` (mastery, calibration), `fixture-guard.awk`
semantics.
*What lands here:* the ±1 stimulus envelope and the ≥66% children-at-stimulus-level rule; span
intersection-over-union < 0.15; the ≥8.0 s inter-item audio gap; no hard delete of a post-draft item;
`done ⇒ at least one skill result`; the single-writer trigger rejecting a mismatched writer; the
monotonic `ProjectionVersion`; no self-approval; read-only triage; the 20-topic and 120-cell shape; and
`INV-6` refusing a demo item in a production exam.

**Seam 4 — `tests/WebMVC.Tests` and `tests/WebMVC.E2ETests/Journeys`**, only for what cannot be asserted
below the view: the display registry (D19), the three-block dashboard, the no-fourth-block constraint, the
two-skill navigation state, and the two mandatory smoke journeys the SRS names (TRN-15): a learner who
has never taken a placement, and a learner mid-path.
*Prior art:* `DashboardThreeBlocksTests`, `AttemptPresentationArchitectureTests`,
`ArchitectureConventionTests`, the `Journeys` page objects.

**No new seam.** The five subsystems differ in domain, not in how their behaviour is observed. The one
shape that could tempt a new seam is the AI grader, and `IAiGradingProcessor` plus
`AiGradingProcessorTests` already cover it — the rebuild changes the rubric data it reads, not the seam.

**Red first, always.** Each ticket's test fails before its change and passes after. For any ticket whose
defect is a prohibition (a constraint that must refuse a write), the test must be shown to fail when the
constraint is removed — the repo has shipped tests that passed because they asserted nothing.

---

## Out of Scope

Carried from SRS §2.3 and from what this spec deliberately does not decide:

- **Pricing and credit economics.** Packages, prices, order reconciliation, payouts. The ledger's
  *structure* is preserved; its *economics* are not in this spec.
- **Mobile.** Desktop-first at 1440×900 is a recorded trade-off. The five mechanisms that break below it
  (split panes, simultaneous listening items under one track, the four-column skill matrix and six-band
  horizon, the 120-cell drilling matrix, the on-screen keyboard during writing) are knowingly broken.
- **Accessibility certification for v1.** The four hooks in D21b are present in the model; enabling them
  is not in this spec.
- **Server-side device storage.** Device identifiers never leave the browser.
- **Audio processing.** No noise suppression, gain normalisation or echo cancellation beyond the
  browser's defaults.
- **Operating-system mixer inspection.** Not detectable, not attempted.
- **Proctoring.** No camera, screen capture, or tab-switch detection.
- **Changing the rubric or scoring *because of* the audio section.** The gate arranges for "not measured"
  to be earned honestly; no criterion, band table, scale, aggregation rule or overall computation changes
  because of it.
- **Authoring the content itself.** ~1,120 production units and 120 lessons at MVR is human and AI-tool
  work with a long lead time. This spec builds the warehouse, the gates and the screens that receive it.
- **Migrating legacy placement results, paths, mastery, insights, items, topics, lessons or rubrics.**
  These are dropped and regenerated (TRN-04); inventing a 1–5 → CEFR mapping is explicitly forbidden.
- **A crosswalk between a certificate band and a CEFR level.** No silent conversion, no defended-source
  conversion either — no such study is in hand.
- **The VSTEP and the two two-skill certificates' target coordinates.** Their feasibility verdict is
  `cannot_judge` until an owner decision populates the coordinate (LVL-16), and this spec does not make
  that decision.

---

## Further Notes

**This spec was rewritten after a full audit, and the audit is the most useful thing in it.** The first
draft cited the SRS by section number instead of carrying its contents. Five parallel audits against the
SRS found roughly **100 dropped requirements, ~20 of them blockers** — every numeric table degraded to a
sentence, most of the fourteen question types reduced to "four-option MCQ", the quarantine thresholds gone
entirely, INV-1 through INV-5 named but never defined, and the six-step dashboard cascade reduced to "a
fixed cascade". Three internal contradictions were found as well: `OverallCefrLevel` stored in one decision
and declared unstorable in another; a false "the SRS states this in no chapter" claim about the eligible
ratio; and an offer to "reduce the form count" that the SRS forecloses by name.

The cause was mechanical and worth naming: **a spec that points is not a spec.** Where the SRS says "×10
items per core cell", a sentence saying "cell depth is a launch concern" loses the number, and the number is
the requirement. D0 exists so this cannot recur silently — the tables live in the spec, and seam 2 asserts
the worked examples computed from them.

Anyone editing this document should check a new claim against D0 first. If a decision needs a number that
is not in D0, the number goes into D0 in the same edit.

**The numbers that are load-bearing and must not be "improved".** 70% correct at a band *and* at every
evaluated band below it; **7** evaluated items per band, because 5 gives 26.27% false demotion, 6 gives
34.46%, and 7 lands on 71.43% against the 70% cutoff; 25% guessing floor, which is why every objective
item is four-option; `τ_low = 0.733` and `τ_high = 0.80` with `n_min = 12`; `γ = 1.5` with a 0.15 floor;
0.15 span overlap; 8.0 s audio gap; 48 h / 72 h expiry; 90-day reuse; 24-hour retake cooldown; 28-day
mastery decay at 0.02 per week. Each of these is derived in the SRS and each one is asserted in a test at
seam 2. A future ticket that changes one of them without recomputing the ones derived from it is the
class of change this list exists to flag.

**A band is not a CEFR level, and the code is where that gets blurred.** The reason `TOEIC 785–940 → B2`
survived is that both quantities were called "band" and lived in one table. The rebuild makes them
different types in different tables (D6), and the test that matters is the one that shows a candidate at
450 Listening / 335 Reading projecting to two different levels on the same certificate.

**Two writers of one fact is the recurring defect, and it is always the same shape:** a derived snapshot
written *beside* its source rather than *from* it. It has happened with placement completion. It will
happen again in this rebuild unless D14 lands early, because placement, mastery, step closure and window
synthesis are all derivations. D14 is therefore in phase 1, not phase 3, even though nothing reads it
until phase 3.

**`DifficultyLevel` is the largest mechanical change and it is boring.** 148 references. Do it whole, in
one ticket, with the schema and every seed and fixture — the repo's own history (D10, D15) shows that
splitting a NOT NULL change across commits turns every subsequent test into a false alarm.

**Content is the long pole and code cannot shorten it.** Phase 1 ships with zero items and proves the
pipeline refuses a bad one; phase 3 needs 456 units; phase 4 needs 1,120. The realistic risk is not that
the engines are wrong but that phase 3 launches against a partly-populated matrix, which CNT-18 and CNT-22
forbid outright. If content slips, the correct response is a narrower certificate and a narrower band
range, not an empty cell.

The corollary is that the first real content proves the whole pipeline in one cell. That is why the phase 2
gate is "one cell drillable end to end" and not a unit total.

**The confirmed defect is the template, not the exception.** `docs/research/placement-free-response-defect.md`
describes a placement that cannot complete because two writers disagree. The rebuild makes free responses
the normal case in placement, so the same defect becomes the normal case unless D5's single writer lands
before placement does any productive work.

**What this spec does not resolve, and who does.** The three pending owner decisions in
`docs/STATE.md` §2 — deleting the remaining non-score-conversion pages, whether certificates differ
structurally or only by parameter (the SRS answers this: structurally, and this spec assumes it), and
whether the credit system survives. Only the third is still open, and it does not block phases 1 and 2.
