# Multi-Certificate Practice Platform — UX Critique
**Date:** 2026-09-27  
**Method:** dual-agent (A: a04ef73df5ae5ce9d · B: a518ff4e3e38dd28d)  
**Target:** prototype/ — 44 HTML screens (student/admin/payment flows)  
**Score:** 30/40 (Good) — improved from 24/40 on 2026-09-20

---

## Executive Summary

**Score trend:** 24 → 30 (out of 40) — 6-point improvement (15% gain)  
**Priority issues:** 2 P0 + 2 P1 + 3 P2 = 7 total  
**Mechanical violations:** 27 findings (em-dash overuse, all-caps text, side-tab borders)  
**Verdict:** Product-authored design with zero drift. Signage concept consistently executed. Major wins: state coverage, financial transparency, visual discipline. Major gaps: dashboard overload, grading anxiety void, no keyboard navigation, minimal help system.

---

## Design Health Score

| # | Heuristic | Score | Key Issue |
|---|-----------|-------|-----------|
| 1 | Visibility of System Status | 3 | Filter states not shown; grading wait lacks time estimate |
| 2 | Match System / Real World | 4 | Speaks Vietnamese learner language; zero jargon |
| 3 | User Control and Freedom | 3 | No package preview before checkout; certificate picker lacks defer option |
| 4 | Consistency and Standards | 4 | Button hierarchy, badge semantics, state patterns never vary |
| 5 | Error Prevention | 3 | Excellent credit/progress protection; weak input validation |
| 6 | Recognition Rather Than Recall | 4 | Context always visible; question navigator shows all states |
| 7 | Flexibility and Efficiency | 2 | No keyboard shortcuts; no bulk actions; single search filter only |
| 8 | Aesthetic and Minimalist Design | 3 | Signage concept is minimal; dashboard/mentor screens overload density |
| 9 | Error Recovery | 3 | Plain language, recovery paths shown; error codes not actionable |
| 10 | Help and Documentation | 1 | Inline explainers only; no persistent help link or FAQ |
| **Total** | | **30/40** | **Good** — Address weak areas, solid foundation |

**Rating band:** Good (28-35/40). Most production interfaces score 20-32. Your prototype sits at 75% — a solid foundation with clear improvement paths.

---

## Design Specificity Verdict

### LLM Assessment: Product-Authored, Not Category-Interchangeable

The "exam-room signage" concept executes **consistently across all 44 screens**. Every surface maintains:

- Hard 2px ink keylines (zero rounded corners, shadows, gradients)
- IBM Plex Sans Condensed ALL CAPS for controls/headers
- Active state inversion to solid ink blocks (never pale tints)
- Four sign colors (red/blue/gold/green) with clear semantics
- Wall grey + painted white maintaining "mounted signage" metaphor

This deliberately **refuses common SaaS patterns** (soft shadows, rounded chips, accent sidebars, pale blue selections). The visual language stays true to its concept—you could not swap these screens into a generic admin template.

**Drift assessment:** Zero drift detected. Even utilitarian screens (admin dashboard, error states, loading skeletons) maintain the flat, square, caps-lock discipline.

### Deterministic Scan: 27 Legitimate Findings

**Total findings:** 38 (excluding 11 false positives in detector script itself)

**Distribution:**
- Quality: 27 findings (71%)
- Slop: 11 findings (29%)
- Accessibility: 0 findings

**Top violations:**
1. **Em-dash overuse** (15 instances, 13 files) — AI cadence tell: 9-28 em-dashes per file in body text
2. **All-caps body text** (11 instances, 6 files) — Long passages (32-55 chars) in uppercase, slowing reading
3. **Side-tab borders** (2 instances) — 4px `border-left` accent on mentor/path cards (AI-generated UI tell)

**Where both assessments agree:**
- Detector flagged em-dash saturation (exam-result.html: 28 em-dashes). Design review independently noted "exam submit modal overwhelms working memory" — both point to copy density issues.
- Detector caught skipped heading levels (h1 → h3 in exam-result.html). Design review scored Accessibility as a concern, calling out screen reader navigation gaps.

---

## Overall Impression

**What works:** This is an uncommonly disciplined prototype. The "exam-room signage" concept carries through 44 screens without compromise — flat keylines, inverted active states, caps-lock labels, and a four-color semantic palette create a visual world that feels purpose-built for Vietnamese test-takers. State coverage is thorough (loading/empty/error/success), financial transparency builds trust (clear credit costs, loss warnings), and typography hierarchy works hard (three Plex cuts split by job: reading/signage/data).

**What doesn't:** The dashboard overloads first-time users with 11 interactive elements simultaneously. The 60-second AI grading wait creates an anxiety void. Keyboard navigation is absent from the primary workflow (200-question exams). Help documentation barely exists beyond inline tooltips. And mechanical quality issues — em-dash saturation, all-caps body text, side-tab accent borders — reveal AI-generated copy that wasn't edited for production.

**Biggest opportunity:** Progressive disclosure on the dashboard and distraction content during grading waits would transform two critical moments (product entry and post-exam peak) from friction points into confidence builders.

---

## What's Working

### 1. Unwavering Design Concept Execution
The "exam-room signage" metaphor is carried through 44 screens without compromise. Hard keylines, caps-lock labels, inversion-based active states, and flat painted fields create a visual world that feels purpose-built, not assembled from a UI kit. This is rare in prototype work. The detector found zero drift across categories — every screen reads as one system.

### 2. State Coverage and Error Handling
Every screen anticipates loading/empty/error/success states with consistent state-block patterns. Edge cases like "exam time expires while submit modal is open" are explicitly handled. The KYC gate placement (before credit deduction, not after) shows careful flow design. Error messages use plain language: "Could not load the test list" not "ERR_FETCH_FAILED".

### 3. Financial Transparency and Loss Prevention
Credit system is explained clearly ("Each credit covers one full 200-question test. Practice by part is always free"). Pricing breaks down per-credit cost (39,800₫/credit for 5-pack). Modals warn before destructive actions ("lose 1 credit"). Duplicate order prevention on checkout. This builds trust in a payment-sensitive context (Vietnamese students on tight budgets).

---

## Priority Issues

### **[P0] Dashboard information overload destroys first-use clarity**

**Screen:** `student/dashboard.html` (success state)

**What:** First successful login shows **11 interactive elements** + 4 dense cards simultaneously. New users don't know where to start. The primary CTA ("Take a mock test now") competes visually with certificate switcher, 3 stats, CEFR chart, accuracy breakdown, and recent exams table.

**Why it matters:** Learners abandon or click randomly. The dashboard is the product's front door — confusion here taints the entire experience. This affects every returning user, every session.

**Fix:** Progressive disclosure. Show only:
1. Welcome + certificate indicator
2. Most recent score (1 stat, not 3)
3. Primary CTA: "Take a mock test" or "Continue learning"
4. Collapsible "View full stats" accordion for CEFR/accuracy/history

**Suggested command:** `/impeccable distill prototype/student/dashboard.html`

---

### **[P0] Grading-pending screen creates 60-second anxiety void**

**Screen:** `student/grading-pending.html`

**What:** After submitting writing, learner sits on a screen that says "This usually takes 30-60 seconds" with an animated progress bar. No countdown. No explanation of what AI is checking. No distraction content. **60 seconds of dead time in a high-anxiety moment.**

**Why it matters:** Perceived wait time feels 2-3× longer. Learners refresh the page or close the tab, potentially breaking the grading process. Post-exam satisfaction drops. This sits at the emotional peak (just finished exam) and creates negative end-of-journey memory.

**Fix:**
1. Add countdown timer: "Estimated 45 seconds remaining"
2. Show live analysis stages: "Checking grammar... ✓ Analyzing vocabulary... ⏳ Scoring coherence..."
3. If wait exceeds 60s: "Still working... Complex responses take up to 2 minutes"
4. Offer distraction: "While you wait: [View your previous scores] [Practice Part 5]"

**Suggested command:** `/impeccable harden prototype/student/grading-pending.html`

---

### **[P1] Exam submit modal overwhelms working memory during time pressure**

**Screen:** `student/exam-confirm-submit.html` (success state with unanswered questions)

**What:** Modal presents 4 simultaneous data points: answered (181), flagged (7), unanswered (19), time remaining (6:31). Learner must process all four, weigh tradeoffs, and decide — while timer is ticking and anxiety is peaking.

**Why it matters:** Suboptimal decisions. Learners either panic-submit (losing easy marks on unanswered questions) or panic-cancel (wasting time re-reading flagged questions that were already correct). This affects score outcomes at the moment of highest stress.

**Fix:** Simplify decision to binary:
- "You have **19 unanswered questions** and **6 minutes 31 seconds** left. That's enough time to answer them."
- CTA: [Go back and finish] vs [Submit now anyway]
- Move "7 flagged" detail to secondary text: "You also flagged 7 questions for review."

**Suggested command:** `/impeccable clarify prototype/student/exam-confirm-submit.html`

---

### **[P1] Em-dash saturation creates AI cadence tell**

**What:** 15 instances across 13 HTML files — detector found 9-28 em-dashes per file in body text. Worst offender: `exam-result.html` with 28 em-dashes. This is an AI writing tell: human copy uses em-dashes sparingly.

**Why it matters:** Undermines product credibility. Vietnamese learners preparing for English exams are sensitive to unnatural English cadence. Copy that reads as AI-generated signals low editorial investment.

**Fix:** Replace em-dashes with commas, colons, periods, or parentheses. Review every instance of "—" and rewrite for plain sentence structure.

**Affected files:**
- exam-result.html (28)
- exam-list.html (12)
- exam-editor.html (12)
- exam-instructions.html (10)
- exam-review.html (10)
- dashboard.html (9)
- exam-history.html (9)
- And 6 more files

**Suggested command:** `/impeccable clarify prototype/`

---

### **[P2] Keyboard navigation absent from exam screens**

**Screen:** `student/exam-reading.html`, `student/exam-listening.html`

**What:** No visible keyboard shortcuts. Learners must click "Next question →" button 200 times per exam. No Tab/Shift+Tab to move between options. No Spacebar to select. No F key to flag.

**Why it matters:** Friction during the primary workflow (taking exams). Power users — repeat exam-takers who are the product's core retention cohort — hit ceiling on efficiency. Heuristic #7 (Flexibility and Efficiency) scored 2/4 primarily due to this gap.

**Fix:**
1. Add keyboard shortcuts: Arrow keys for prev/next, 1-4 for A-D, F to flag, Enter to submit
2. Show shortcut hints on first exam: "Tip: Use arrow keys to navigate questions"
3. Add keyboard shortcut reference in exam topbar (icon + modal)

**Suggested command:** `/impeccable harden prototype/student/exam-reading.html`

---

### **[P2] All-caps body text slows reading speed**

**What:** 11 instances across 6 HTML files — detector found 32-55 character passages set in uppercase (`text-transform: uppercase` on body text). Found in exam-editor, dashboard, learning-path screens.

**Why it matters:** Uppercase breaks word shape recognition, measurably slowing reading. This is especially harmful in timed exam contexts where scan speed matters. WCAG 2.1 discourages all-caps for readability.

**Fix:** Reserve uppercase for short labels only (buttons, badges, nav items). Drop to sentence case in condensed bold face for any string longer than a few words — state titles, stepper text, catalog names, label descriptors (already documented in DESIGN.md "Caps-On-A-Ground Rule").

**Affected files:**
- admin/exam-editor.html (3 instances)
- student/dashboard.html (3 instances)
- student/learning-path.html (2 instances)
- student/weakness-analysis.html (2 instances)
- admin/kyc-review.html (1 instance)

**Suggested command:** `/impeccable typeset prototype/`

---

### **[P2] Side-tab accent borders break the signage concept**

**What:** 2 instances — detector found 4px `border-left` colored bars on cards in `mentor-confirm.html` and `path-detail.html`. This is the most recognizable tell of AI-generated UIs and directly contradicts the "exam-room signage" concept.

**Why it matters:** The visual world refuses pale-blue SaaS defaults (soft shadows, tinted chips, **accent sidebars**). Side-tabs are explicitly banned in DESIGN.md: "Don't put a coloured `border-left` or `border-right` above 1px on a card, alert, list item or callout. The alert used to be a 4px left bar; it is the category's most recognisable tell."

**Fix:** Remove `border-left: 4px`. If accent is needed for semantic grouping, use the system's approach: a 2px ink keyline around the entire card, or a sign-color wash background with matching keyline (like the alert pattern).

**Suggested command:** `/impeccable polish prototype/student/mentor-confirm.html prototype/student/path-detail.html`

---

## Persona Red Flags

### **Jordan (First-Timer): Confused at Every Entry Point**

**Primary action:** Sign up → Pick certificate → Take first practice session

**Failures:**

1. **Dashboard first-time state (certificate picker):** Three certificate cards with full names ("Test of English for International Communication") but no guidance on which to choose. A 17-year-old Vietnamese high school student doesn't know the difference between TOEIC vs IELTS vs VSTEP without research. No "Help me choose" link. **Jordan guesses wrong and wastes 30 minutes on the wrong test.**

2. **No onboarding flow:** Jordan lands on dashboard and must figure out that Practice is free but Exams cost credits. The pricing page explains this, but Jordan doesn't know to visit pricing first. **Jordan clicks "Take a mock test" expecting free practice, hits paywall, feels tricked.**

3. **Practice-select screen:** Does Jordan know what "Part 5 — Incomplete Sentences" means? First-timers need primer: "Part 5 tests grammar. 30 questions. Good for beginners." **Jordan picks Part 7 (hardest) because the name sounds interesting, gets discouraged.**

**Impact:** Abandonment at critical onboarding moments.

---

### **Sam (Accessibility-Dependent): Cannot Use Product Independently**

**Primary action:** Navigate with screen reader, take exam with keyboard

**Failures:**

1. **Question navigator grid** (`student/exam-reading.html`): 200 cells rendered as `<span>` with `is-answered`/`is-unanswered` classes. No ARIA labels. Screen reader announces "101, 102, 103..." with no context. **Sam doesn't know which questions are answered vs unanswered without sighted help.**

2. **CEFR band chart** (`student/dashboard.html`): `role="img"` with `aria-label` is good, but the 6 bands are not individually labeled. Sam hears "A1, A2, B1, B2..." but doesn't know which is current vs reached. **Sam cannot track progress.**

3. **Timer countdown:** `role="timer"` exists but no announcement when time hits critical thresholds (10 minutes left, 5 minutes left). **Sam relies on periodic screen reader refresh to check time, loses focus on exam.**

4. **Modal overlay keyboard trap:** Modals lack `inert` attribute on background content. When exam-confirm-submit modal is open, Tab key can still cycle to sidebar nav behind the modal. **Sam gets disoriented, cannot find submit button.**

**Impact:** Sam cannot use the product independently. This is a **legal compliance risk** (WCAG 2.1 AA minimum for educational platforms in many jurisdictions).

---

### **Casey (Mobile User): Desktop-Only Design Blocks Growth**

**Primary action:** Take exam on phone during commute

**Failures:**

1. **Exam layout is two-column** (passage left, questions right). On mobile, this stacks vertically, forcing Casey to scroll up/down repeatedly. TOEIC Part 7 passages are 200+ words. **Unusable on mobile.**

2. **Question navigator sidebar** collapses off-screen on mobile, removing progress visibility. **Casey can't see how many questions remain without opening a menu.**

3. **Timer in topbar** might be cut off on narrow screens. **Casey misses time warnings, runs out of time unexpectedly.**

4. **Dashboard information overload is worse on mobile:** 11 interactive elements become a single-column scroll list of 15+ viewport heights. **Casey abandons mobile usage entirely.**

**Note:** Prototype states "Desktop-first (1440×900)" so mobile failures are expected, but persona testing reveals the cost: **Vietnamese users have high mobile usage**. Desktop-only limits market reach.

---

## Minor Observations

1. **Sidebar nav "ID verification" link** appears even after KYC is approved. Should hide post-approval or change to "Verification status: Approved ✓"

2. **Exam-list "kyc-pending" alert** says "Usually within 1 business day." Sunday submissions wait 2 days. Consider "1-2 business days" or show expected approval date.

3. **Pricing page "Popular" badge** on Exam Prep package — is this data-driven? If not, remove. False urgency erodes trust.

4. **Admin dashboard alerts** use inconsistent severity. "2 tests missing audio" should be danger (blocks publishing), not warning.

5. **Mentor card star ratings** use Unicode ★ characters that don't scale well. Replace with accessible component: "4.5 out of 5 stars (20 reviews)".

6. **Weakness analysis "Frequently missed question types"** shows stub message. Either remove entirely or show believable mock data.

7. **Progress bars** use color-coded severity (danger/warning/success). Excellent semantic use, but ensure colorblind users can distinguish — consider adding icons.

8. **Skipped heading level** in exam-result.html (h1 → h3). Detector caught this; breaks screen reader document outline.

9. **Certificate switcher button** uses `btn--plate` variant not seen in components.css. Needs CSS definition or replacement with documented variant.

10. **Table striping** contrast is subtle (wall grey vs painted white). Ensure it meets WCAG 1.4.1 if row color carries meaning.

---

## Questions to Consider

1. **Does the exam-room signage concept scale to IELTS Writing (essay editing UI) and Speaking (recording UI)?** The concept works beautifully for multiple-choice. But writing requires rich text input, character counting, editing tools — can hard keylines survive in a text editor without feeling oppressive?

2. **Is the credit system the right metaphor for Vietnamese learners?** Vietnamese education uses "buổi học" (sessions) rather than abstract credits. Would "5 exam sessions" resonate more?

3. **Why does the primary CTA say "Take a mock test now" instead of "Start practicing"?** "Mock test" implies high stakes (deducts credit). New learners might hesitate. Which drives more first-time engagement?

4. **Can the CEFR band chart double as a motivation mechanic?** Right now it's informational. What if each band unlocked a reward (badge, discount, mentor session)?

5. **Should AI grading show its confidence score?** If AI grades writing as 6.5/9, does showing "85% confidence" build trust or create doubt?

6. **What happens to the design when VSTEP Speaking requires video recording?** Video preview, camera permissions, playback controls — these are inherently soft, rounded UI patterns. Can you integrate a video player without breaking immersion?

7. **Why is "Practice by section" free but "Mock exams" cost credits?** Business model question with UX implications. Shouldn't the first mock be free (with credit gate at #2)?

8. **Can mentors be integrated into the weakness analysis flow?** Weakness analysis identifies Part 7 as weakest — why not offer "Book a mentor for Part 7" CTA directly in that card?

---

## Recommended Action Plan

Based on user direction: **Critical UX first**, **stay bold with signage concept**, **full quality pass**.

### Command Sequence (Priority Order)

1. **`/impeccable distill prototype/student/dashboard.html`**  
   Strip dashboard to essentials. Fixes P0 first-use confusion.

2. **`/impeccable harden prototype/student/grading-pending.html`**  
   Add countdown + live stages + distraction. Fixes P0 anxiety void.

3. **`/impeccable clarify prototype/student/exam-confirm-submit.html`**  
   Simplify modal to binary decision. Fixes P1 working memory overload.

4. **`/impeccable clarify prototype/`**  
   Fix em-dash saturation across 13 files. Fixes P1 AI writing tell.

5. **`/impeccable harden prototype/student/exam-reading.html`**  
   Add keyboard shortcuts. Fixes P2 power user efficiency.

6. **`/impeccable typeset prototype/`**  
   Drop all-caps body text to sentence case. Fixes P2 readability.

7. **`/impeccable polish prototype/student/mentor-confirm.html prototype/student/path-detail.html`**  
   Remove side-tab borders. Fixes P2 visual drift.

8. **`/impeccable audit prototype/`**  
   Add ARIA labels, fix keyboard traps, fix headings. Fixes accessibility compliance.

9. **`/impeccable critique prototype/`**  
   Re-run to measure improvement. Target: 36+ (Excellent range).

---

## Projected Outcomes

- **Score:** 30 → 36+ (Good → Excellent)
- **P0 issues:** 2 → 0 (blocks removed)
- **P1 issues:** 2 → 0 (critical debt cleared)
- **P2 issues:** 3 → 0 (quality ceiling reached)
- **Detector violations:** 27 → ~5 (only false positives remain)
- **Accessibility:** Sam can use product independently; WCAG 2.1 AA compliant
- **Design consistency:** Signage concept stays bold and uncompromised

**Estimated effort:** 6-8 hours focused work across all commands.

---

## Appendix: Assessment Methodology

**Method:** Dual-agent isolated assessment (Impeccable critique protocol)

**Assessment A (Design Review):**
- Evaluated Nielsen's 10 heuristics (0-4 scale each)
- Cognitive load assessment (8-item checklist)
- Emotional journey analysis (peak-end rule, anxiety valleys)
- Persona walkthroughs (Jordan, Sam, Casey)
- Design specificity verdict (product-authored vs category-interchangeable)

**Assessment B (Detector Evidence):**
- CLI mechanical scan: 44 HTML files + CSS
- 38 findings detected (27 legitimate, 11 false positives)
- Categorized by type (quality/slop/accessibility) and severity (warning/advisory)
- No browser visualization performed (CLI data sufficient)

**Synthesis:**
- Combined both assessments after independent completion
- Identified where LLM review and detector agreed
- Flagged false positives
- Prioritized issues by severity (P0-P3) and impact

**Files Reviewed:**
- Representative screens across student/admin/payment sections
- DESIGN.md (visual system reference)
- PRODUCT.md (business context and constraints)
- 44 HTML prototypes + shared CSS components

---

**Critique stored:** `.impeccable/critique/2026-09-27T11-01-26Z__prototype.md`
