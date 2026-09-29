# Scroll Pinning Strategy — extra efficient

## Current Implementation (Already Sticky)

### ✅ Exam Sidebar (exam-reading.html, exam-listening.html)
- **Element**: `.exam-sidebar`
- **Position**: `position: sticky; top: var(--space-4);`
- **Reason**: Keeps question navigator, progress, and timer visible during long passages
- **Location**: `components.css:645`

### ✅ Context Bar (dashboard, exam-list, exam-result, pricing)
- **Element**: `.context-bar`
- **Position**: `position: sticky; top: 0; z-index: 100;`
- **Reason**: Always shows Certificate, Credits, Progress context
- **Location**: `components.css:1268`

## Recommended Additions

### Should Pin:

1. **Exam Header Timer (during exam)**
   - **Where**: Top bar with timer during exam-reading/listening
   - **Why**: Critical time remaining must stay visible
   - **Current**: In topbar but not independently sticky
   - **Action**: Already in sticky topbar, no change needed

2. **Payment Summary (checkout.html)**
   - **Where**: Order summary sidebar
   - **Why**: Keep total and package details visible while scrolling terms
   - **Action**: Add `position: sticky; top: calc(var(--topbar-height) + var(--space-4));` to checkout sidebar

3. **Learning Path Progress (learning-path.html)**
   - **Where**: Progress indicator showing completion
   - **Why**: Long path needs visible progress reference
   - **Action**: Consider sticky progress bar at top of content area

### Should NOT Pin:

1. **Navigation Sidebar**
   - Already accessible via scroll
   - Doesn't change during use
   - Full height visibility not critical

2. **Empty State Cards**
   - Transient states
   - No user action while visible

3. **Form Headers**
   - Short forms don't need pinned headers
   - Context doesn't change mid-form

## Z-Index Hierarchy

```
100: context-bar (below topbar, above content)
 50: exam-sidebar (above content, below overlays)
 10: sticky section headers (if added)
  1: default sticky elements
```

## Mobile Considerations

- Sticky elements consume viewport height
- On mobile (<640px), exam-sidebar becomes static (see components.css:657)
- Context bar may stack items vertically on narrow screens
- Consider collapsing sticky bars on scroll down, expanding on scroll up

## Performance Notes

- `position: sticky` is GPU-accelerated, performant
- Use `will-change: transform` sparingly, only during scroll
- Keep sticky elements shallow in DOM (avoid nested sticky)
