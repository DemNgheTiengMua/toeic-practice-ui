# Student Dashboard Guidance Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the exploratory Score & Band Simulator from the Student Dashboard (`/dashboard`) and re-architect the interface into a warm, high-clarity **AI-guided command center** where learners receive empathetic, deterministic direction, clear progress tracking, and sequential daily milestones—strictly honoring the *"Bảng hiệu phòng thi"* design system.

---

## File Structure & Responsibilities

| File | Primary Responsibility |
|---|---|
| `src/WebMVC/Views/Dashboard/Index.cshtml` | Main dashboard view: Orchestrates layout hierarchy (Sentinel → Target Runway → AI Mentor & Daily Directive Grid → Weakness Radar → History & Mentor Row). Removes `_DashboardSimulator`. |
| `src/WebMVC/Views/Dashboard/_DashboardMission.cshtml` | Redesigned from a 3-column horizontal card block into a clean, vertical 3-step sequential milestone directive that pairs beside the AI Mentor. |
| `src/WebMVC/Views/Shared/_AiInsightPanel.cshtml` | Elevated into the **AI Study Mentor Companion** with empathetic framing, clear status/trend badges, actionable recommendation chips, and dedicated new-learner onboarding guidance. |
| `src/WebMVC/Views/Dashboard/_DashboardWeaknessRadar.cshtml` | Streamlined diagnostic radar focusing on critical bottlenecks with clear tier badges and links to full analysis. |
| `src/WebMVC/wwwroot/css/components.css` | Consolidates all dashboard styling into unified, token-compliant rules (`border-radius: 0`, 2px ink keylines, signage colors), eliminating scattered `<style>` tags. |
| `tests/WebMVC.Tests/DashboardControllerTests.cs` | Controller unit tests ensuring dashboard models, insight pre-fetching, and view rendering pass without errors. |

---

## Tasks

### Task 1: Consolidate Dashboard Design Tokens & Layout CSS

**Context:** The dashboard views currently have inline `<style>` tags in 5 separate partials. We must centralize them into `components.css` with dedicated classes for the guidance grid, the AI mentor card, the vertical mission checklist, and the target runway.

- [ ] **Step 1: Inspect existing dashboard classes in `src/WebMVC/wwwroot/css/components.css`**
  Review lines 2110–2230 where `.readiness-strip` and `.dashboard-bento` are defined.
- [ ] **Step 2: Add consolidated CSS classes in `components.css`**
  Define:
  - `.dashboard-guidance-grid`: 2-column responsive layout (`minmax(0, 7fr) minmax(320px, 5fr)` on desktop, 1-column on mobile `<= 1024px`).
  - `.ai-mentor-panel`: Elevated card with distinctive 4px Sign Blue left keyline, warm header placard (`AI STUDY MENTOR · YOUR @cert.Code GUIDE`), empathetic narrative typography, and priority recommendation chips.
  - `.mission-checklist`: Vertical sequential 3-step milestone board with step numbers (`STEP 01`), status indicators (Current, Cleared, Upcoming), and focused CTA buttons.
  - Strict compliance with `DESIGN.md`: `border-radius: 0`, `box-shadow: none`, `2px solid var(--ink)`, `IBM Plex` typography tokens.
- [ ] **Step 3: Verify CSS build and formatting**
  Run `dotnet build` to confirm solution compiles.

---

### Task 2: Elevate AI Insight Panel into Personal Study Mentor (`_AiInsightPanel.cshtml`)

**Context:** The user explicitly noted: *"the AI insight is very important since it provide guidance, makes they feel not alone"*. Currently, the panel shows technical phrases like "CLAUDE AI // COGITATING" or "Model: fake-ai-insight-v1". We must reframe it as a dedicated, reassuring AI Study Mentor.

- [ ] **Step 1: Enhance header and companion framing**
  Update `src/WebMVC/Views/Shared/_AiInsightPanel.cshtml`:
  - Header title: `<span class="badge badge--info font-mono uppercase font-bold">AI Mentor</span> <strong>Personal Diagnostic Guidance · @(certCode ?? "Your Certificate")</strong>`
  - Reassuring companion tagline: *"Continuous AI diagnostic review over your answers and score trajectory — you're never studying alone."*
  - Reframe loading phase: Instead of random novelty words, display meaningful coaching steps: *"AI Mentor is analyzing your recent answer traps..."*, *"Evaluating accuracy against your target band..."*.
- [ ] **Step 2: Add warm encouragement for new / uncalibrated learners**
  When `insight == null` and status is not pending/failed (e.g. brand new user with 0 practice sessions):
  - Render an empathetic welcome card:
    *"Welcome to your preparation journey! As your AI Study Mentor, I'm here to analyze your practice, spot question traps, and guide you directly to your target band. Start with a quick free Placement Test so I can calibrate your baseline."*
    Include direct action: `<a class="btn btn--primary" href="/placement">Begin Diagnostic Placement ›</a>`.
- [ ] **Step 3: Streamline recommendations and narrative presentation**
  - Ensure recommendations clearly show priority chips (`Priority`, `Focus`, `Tip`).
  - Keep rubric highlights prominent for writing/speaking attempts.
  - Remove intrusive technical model strings from the main view (keep in small metadata footnote or title tooltip).
- [ ] **Step 4: Verify rendering with existing tests**
  Run `dotnet test --filter DashboardControllerTests`.

---

### Task 3: Refactor Today's Mission into a Vertical Milestone Checklist (`_DashboardMission.cshtml`)

**Context:** The current 3-column horizontal mission board spans the full width and takes up massive horizontal space with redundant cards. Refactoring it into a vertical sequential directive allows it to sit cleanly alongside the AI Mentor in the guidance grid.

- [ ] **Step 1: Redesign `_DashboardMission.cshtml` layout**
  Transform the internal grid into a vertical list of milestones:
  - Header: `"Today's Directive · 3-Step Milestone Checklist"`, with cleared count badge (`X / 3 CLEARED`).
  - Sequential cards/rows:
    - `STEP 01`, `STEP 02`, `STEP 03` numbered tags.
    - Milestone title and brief description.
    - Metadata and direct CTA button (`btn--primary` for active step, ghost for cleared).
    - Status badge: `badge--success` ("Cleared") or `badge--info` ("Active").
- [ ] **Step 2: Clean up embedded inline styles**
  Remove the heavy inline `<style>` tag in `_DashboardMission.cshtml` in favor of the centralized classes in `components.css`.
- [ ] **Step 3: Test layout responsiveness**
  Ensure the vertical checklist wraps cleanly on tablet and mobile viewports.

---

### Task 4: Restructure `Index.cshtml` & Remove Simulator

**Context:** Remove `_DashboardSimulator` completely from the template, restructure the page into logical zones, and position the AI Mentor + Mission Directive at the core of the dashboard.

- [ ] **Step 1: Remove `_DashboardSimulator` call**
  In `src/WebMVC/Views/Dashboard/Index.cshtml`, remove line 220:
  `<partial name="_DashboardSimulator" model="dashboard" />`.
- [ ] **Step 2: Build the Core Guidance Grid**
  Replace the stacked bento layout with the new 2-column Guidance Grid:
  ```html
  <div class="dashboard-guidance-grid mt-4">
    <div class="dashboard-guidance__mentor">
      <partial name="_AiInsightPanel" model='...' />
    </div>
    <div class="dashboard-guidance__directive">
      <partial name="_DashboardMission" model="dashboard" />
    </div>
  </div>
  ```
- [ ] **Step 3: Polish the Target Runway (`.readiness-strip`)**
  Ensure the readiness strip clearly displays the target gap (e.g. `-230 pts to B2`) with high visual contrast so the learner instantly sees their distance to goal.
- [ ] **Step 4: Streamline Weakness Radar & Lower Grid**
  Position `_DashboardWeaknessRadar` below the guidance grid as a focused diagnostic table, followed by Recent Exams/Practice and the Mentor Boarding Pass.
- [ ] **Step 5: Verify new learner flow (`IsNew == true`)**
  Ensure the "Start your journey" onboarding guidance is elevated and harmonized with the AI Mentor's welcome message.

---

### Task 5: End-to-End Verification & Visual Audit

- [ ] **Step 1: Execute full test suite**
  Run `dotnet test` across the entire solution.
- [ ] **Step 2: Run crawl environment / local build verification**
  Run `dotnet build` to ensure clean build with zero errors or warnings.
- [ ] **Step 3: Visual inspection checklist**
  - [ ] No Simulator sliders or calculation boxes rendered on `/dashboard`.
  - [ ] AI Study Mentor is positioned prominently in the upper half of the dashboard.
  - [ ] AI Mentor copy conveys reassurance and guidance ("you're not alone").
  - [ ] Today's Directive presents a clear 3-step sequential path.
  - [ ] Target Band gap is immediately readable in the readiness strip.
  - [ ] Zero border-radius, zero box-shadows, 2px ink keylines strictly maintained.
