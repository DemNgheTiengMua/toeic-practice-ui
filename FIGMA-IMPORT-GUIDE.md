# TOEIC Practice UI - Figma Import Guide

This guide explains how to import the TOEIC exam interface prototype into Figma using HTML-to-Figma plugins, with detailed instructions for preserving the exam-hall signage design system.

## Recommended Plugins

### Primary Option: html.to.design
**Best for:** Complete prototype import with styles preserved

**Installation:**
1. Open Figma desktop or web app
2. Go to Community → Plugins
3. Search for "html.to.design"
4. Click "Install" or "Try it out"

**Pricing:** Free tier available, Pro features for complex imports

### Alternative: Figma Import
**Best for:** Simpler imports, good for individual screens

**Installation:**
1. In Figma, go to Plugins → Browse plugins in Community
2. Search for "Figma Import"
3. Install the plugin

## Pre-Import Preparation

### Step 1: Serve Your Prototype Locally

You need a local web server to access the HTML files. Choose one method:

#### Option A: Using Python (if installed)
```bash
cd "D:\Study\PRN3\Project\extra_efficiency_ui\.claude\worktrees\critique-report\prototype"
python -m http.server 8000
```
Then open: `http://localhost:8000/student/exam-reading.html`

#### Option B: Using Node.js (if installed)
```bash
npm install -g http-server
cd "D:\Study\PRN3\Project\extra_efficiency_ui\.claude\worktrees\critique-report\prototype"
http-server -p 8000
```
Then open: `http://localhost:8000/student/exam-reading.html`

#### Option C: Using VS Code Live Server Extension
1. Install "Live Server" extension in VS Code
2. Right-click on `exam-reading.html`
3. Select "Open with Live Server"

### Step 2: Prepare Screens for Import

**Priority screens to import first:**
1. `exam-reading.html?state=success` - Main reading exam interface
2. `exam-listening.html?state=success` - Main listening exam interface
3. `exam-confirm-submit.html?state=success` - Submission modal (has unanswered questions)
4. `exam-confirm-submit.html?state=complete` - Submission modal (all answered)
5. `exam-result.html` - Score display
6. `exam-review.html?state=success` - Answer review with explanations

**Note the state parameter:** Many screens use `?state=<name>` to show different states. You'll need to import each state separately.

## Import Process with html.to.design

### Step 1: Launch the Plugin

1. In Figma, create a new file or open existing project
2. Go to Plugins → html.to.design
3. The plugin panel will open

### Step 2: Import Your First Screen

1. **Enter the URL** in the plugin:
   ```
   http://localhost:8000/student/exam-reading.html?state=success
   ```

2. **Configure Import Settings:**
   - ✅ Import as components
   - ✅ Preserve text styles
   - ✅ Import CSS variables
   - ✅ Create color styles
   - ⚠️ Be careful with "Flatten groups" - keep unchecked for exam-hall design

3. **Click "Import"**
   - The plugin will fetch and convert your HTML/CSS to Figma layers
   - Wait for the import to complete (30-60 seconds typical)

4. **Review the imported artboard**
   - The screen should appear on your Figma canvas
   - Check that colors, typography, and layout are preserved

### Step 3: Import Additional Screens

Repeat Step 2 for each screen and state:
- exam-listening.html?state=success
- exam-confirm-submit.html?state=success
- exam-confirm-submit.html?state=complete
- exam-confirm-submit.html?state=expired
- exam-result.html
- exam-review.html?state=success

### Step 4: Import the State Switcher

For screens with multiple states, import each state variation:
```
exam-reading.html?state=success
exam-reading.html?state=loading
exam-reading.html?state=expired
exam-reading.html?state=offline
```

The state switcher at the bottom shows all available states.

## Post-Import Manual Adjustments

After importing, you'll need to make some manual refinements to ensure design system accuracy:

### 1. Verify Design Tokens

**Colors - Check these exact values:**
```
Sign Red: #B31217
Sign Blue: #0B4EA2
Sign Gold: #F2B705
Sign Green: #0A6B3C
Ink: #14181D
Wall: #EEF0F1
Painted (white): #FFFFFF
Line Soft: #CCD2D6
Ink Muted: #5C6570
```

**How to verify:**
1. Select an element with sign-blue background
2. Check fill color in right panel
3. If it doesn't match #0B4EA2 exactly, create a color style and apply it
4. Repeat for all brand colors

### 2. Create Typography Styles

The plugin may import fonts but not create reusable text styles. Create these:

**Display (Score placard):**
- Font: IBM Plex Mono
- Size: 56px
- Weight: Bold (700)
- Line height: 100%
- Letter spacing: -3%

**Headline (h1 - Section titles):**
- Font: IBM Plex Sans Condensed
- Size: 28px
- Weight: Bold (700)
- Line height: 115%
- Letter spacing: 5.5%
- Transform: UPPERCASE

**Title (h2):**
- Font: IBM Plex Sans Condensed
- Size: 22px
- Weight: Bold (700)
- Line height: 115%
- Letter spacing: 5.5%
- Transform: UPPERCASE

**Body (Default):**
- Font: IBM Plex Sans
- Size: 16px
- Weight: Regular (400)
- Line height: 160%

**Read (Question stems, passages):**
- Font: IBM Plex Sans
- Size: 18px
- Weight: Regular (400)
- Line height: 170%

**Label (Buttons, badges, headers):**
- Font: IBM Plex Sans Condensed
- Size: 12px
- Weight: Bold (700)
- Line height: 115%
- Letter spacing: 5.5%
- Transform: UPPERCASE

**Data (Clock, scores, numbers):**
- Font: IBM Plex Mono
- Size: 14px
- Weight: Regular (400)
- Line height: 160%
- Variant: Tabular nums

**How to create:**
1. Select text element
2. In right panel, click "Text" section
3. Click "+" next to "Text styles"
4. Name it (e.g., "Body/Regular")
5. Apply to all matching text

### 3. Fix Border Radius (Critical!)

**The exam-hall signage system uses square corners everywhere.**

The plugin might round some corners. Fix this:
1. Select all imported frames (Cmd/Ctrl + A)
2. In right panel, set Corner radius: 0
3. Check buttons, cards, inputs, badges - all should be square

### 4. Verify Keylines (Borders)

**Two keyline weights exist:**
- **2px solid ink (#14181D)** - Panel frames, sign borders
- **1px solid line-soft (#CCD2D6)** - Interior divisions

**How to fix:**
1. Select an element with border
2. Check stroke weight and color in right panel
3. Create stroke styles:
   - "Keyline/Heavy" - 2px, #14181D, inside
   - "Keyline/Soft" - 1px, #CCD2D6, inside
4. Apply appropriate style to each element

### 5. Create Component Structure

Organize imported elements into reusable components:

#### Button Components
Create variants for:
- **Type:** Default (plate), Primary (blue), Danger (red), Ghost
- **Size:** Default, Large, Small
- **State:** Default, Hover, Disabled, Focus

**How to create:**
1. Select a button element
2. Right-click → "Create component"
3. Name it "Button"
4. Click "+" next to component name to add variants
5. Configure variant properties (Type, Size, State)
6. Duplicate and style each variant

#### Other Components to Create
- **Badge** (variants: Default, Success, Warning, Danger, Info, Muted)
- **Card/Panel** (with header, body, footer slots)
- **Field/Input** (variants: Default, Focus, Invalid)
- **Option** (answer choice variants: Default, Selected, Correct, Wrong)
- **Navigator Cell** (variants: Unanswered, Answered, Marked, Current)
- **Timer** (variants: Default, Warning, Critical)
- **Modal** (with overlay, header, body, footer)
- **Alert** (variants: Info, Success, Warning, Danger)

### 6. Set Up Auto Layout

The plugin may not create Auto Layout frames. Add them manually:

**For buttons:**
1. Select button frame
2. Press Shift + A (or right-click → Add Auto Layout)
3. Set padding: 8px vertical, 16px horizontal
4. Set gap: 8px (between icon and text)
5. Set alignment: center

**For card bodies:**
1. Select card body frame
2. Add Auto Layout
3. Set padding: 24px all sides
4. Set gap between children: 16px or 24px (var(--space-4) or var(--space-5))
5. Direction: vertical

**For option lists:**
1. Select option list container
2. Add Auto Layout
3. Direction: vertical
4. Gap: 8px (var(--space-2))

### 7. Verify Spacing Scale

Check that spacing matches the 4px scale:
- 4px (space-1)
- 8px (space-2)
- 12px (space-3)
- 16px (space-4)
- 24px (space-5)
- 32px (space-6)
- 48px (space-7)

**Audit spacing:**
1. Select container frames
2. Check padding values
3. Check gaps in Auto Layout
4. Adjust to nearest scale value if off

### 8. Import IBM Plex Fonts

If fonts don't load correctly:

1. **Download IBM Plex:**
   - Go to https://github.com/IBM/plex/releases
   - Download latest release (e.g., "IBM-Plex-Sans-v6.0.0.zip")
   - Extract fonts

2. **Install fonts on your system:**
   - Open IBM Plex Sans folder
   - Install all weights: Regular, SemiBold, Bold
   - Open IBM Plex Sans Condensed folder
   - Install all weights
   - Open IBM Plex Mono folder
   - Install all weights

3. **Refresh Figma:**
   - Restart Figma
   - Fonts should now be available

4. **Reapply fonts to text:**
   - Select all text (Cmd/Ctrl + A)
   - In font dropdown, select appropriate IBM Plex variant
   - Update text styles

### 9. Handle State Variations

For screens with multiple states (loading, success, error, etc.):

1. **Import each state separately** as shown in Step 3
2. **Create a component set:**
   - Select all state artboards for one screen
   - Right-click → "Create component set"
   - Name it (e.g., "Exam Reading Screen")
3. **Add variant property:**
   - In right panel, add property "State"
   - Options: Success, Loading, Expired, Offline, etc.
4. **Configure each variant:**
   - Show/hide appropriate layers
   - Update content (e.g., timer values, badges)

### 10. Verify Interactive States

The plugin imports the default state. You need to manually create other states:

**For buttons:**
1. Duplicate Default variant
2. Rename to "Hover"
3. Apply hover styles:
   - Default button: Ink background (#14181D), white text
   - Primary button: Darker blue (#093D80)
   - Danger button: Darker red (#8C0E12)

**For options:**
1. Create Selected variant: Ink marker background
2. Create Correct variant: Green border + marker
3. Create Wrong variant: Red border + marker

## Creating a Design System in Figma

After importing and cleaning up, organize into a proper design system:

### 1. Create a "Design System" Page

1. Click "+" next to pages
2. Name it "🎨 Design System"
3. Move all components here

### 2. Organize Components into Sections

Create frames for each category:
- **🎨 Colors** - All color styles as swatches
- **📝 Typography** - All text styles with examples
- **🔘 Buttons** - All button variants
- **📦 Cards & Panels** - Panel components
- **📋 Forms** - Inputs, options, fields
- **🧭 Navigation** - Navigator cells, steppers
- **⚠️ Feedback** - Alerts, toasts, state blocks
- **⏱️ Indicators** - Timers, progress bars, badges
- **🔲 Modals** - Modal overlays and content

### 3. Create Color Styles

1. Go to local styles (four squares icon in toolbar)
2. Click "+"
3. Create folders:
   - **Brand** (Sign Red, Sign Blue, Sign Gold, Sign Green)
   - **Neutral** (Ink, Wall, Painted, Line Soft, Ink Muted)
   - **Status** (with washes: Info, Success, Warning, Danger)
4. For each color:
   - Click "+" under appropriate folder
   - Name it (e.g., "Brand/Sign Blue")
   - Set hex value
   - Add description from DESIGN.md

### 4. Document Design Decisions

Add text frames with documentation:
- **Creative North Star:** "Exam-room signage"
- **Key Rules:**
  - "One flat field rule" (no gradients)
  - "Inversion rule" (active = solid ink)
  - "Square corner rule" (border-radius: 0)
  - "Field is not lettering" (use darkened ink colors for text)
- **Typography Rules:**
  - "Width is meaning" (mono for numbers)
  - "Caps on a ground" (uppercase only on labeled grounds)
- **Layout Rules:**
  - "4px spacing scale"
  - "Desktop-first at 1440×900"

## Screen Organization in Figma

Create a logical page structure:

### Page 1: 🏠 Cover
- Project title
- Design system overview
- Link to DESIGN.md documentation

### Page 2: 🎨 Design System
- All components organized by category
- Color palette
- Typography scale
- Spacing system
- Icon set (if any)

### Page 3: 📱 Student Screens
Organize by flow:
- **Exam List** → exam-list.html
- **Instructions** → exam-instructions.html
- **Listening Exam** → exam-listening.html (all states)
- **Reading Exam** → exam-reading.html (all states)
- **Confirm Submit** → exam-confirm-submit.html (all states)
- **Results** → exam-result.html
- **Review** → exam-review.html (all states)
- **History** → exam-history.html

### Page 4: 🔐 Authentication
- Login, register, forgot password screens

### Page 5: 💳 Payment
- Credit package selection
- Payment gateway screens
- Order confirmation

### Page 6: 👤 Profile & Settings
- Profile, weakness analysis, KYC screens

### Page 7: 👨‍💼 Admin Screens
- Admin dashboard
- Question authoring
- KYC review queue
- User management

## Responsive Frames

Create frames at key breakpoints:

**Desktop (Primary):**
- 1440×900 - Primary target viewport
- 1920×1080 - Common large desktop

**Tablet:**
- 768×1024 - Tablet portrait (new breakpoint)
- 1024×768 - Tablet landscape

**Mobile:**
- 390×844 - iPhone 12/13/14 Pro
- 360×800 - Common Android size

**How to set up:**
1. Create frame for each breakpoint
2. Import same screen URL for each size
3. Verify responsive behavior matches CSS breakpoints
4. Document which elements stack, hide, or resize

## Troubleshooting Common Import Issues

### Issue 1: Fonts Don't Load

**Symptoms:** Text shows in Arial or Helvetica instead of IBM Plex

**Fix:**
1. Install IBM Plex fonts on your system (see Step 8 above)
2. Restart Figma
3. Select all text
4. Change font family to IBM Plex Sans/Condensed/Mono

### Issue 2: Colors Are Slightly Off

**Symptoms:** Blues look too bright, reds look off

**Fix:**
1. Check hex values against DESIGN.md
2. Plugin may have converted colors to RGB differently
3. Manually set exact hex values
4. Create color styles to ensure consistency

### Issue 3: Layout Doesn't Match

**Symptoms:** Elements overlap or spacing is wrong

**Fix:**
1. Check that Auto Layout was applied
2. Verify padding and gap values
3. May need to recreate layout structure manually
4. Use Figma's grid (8px baseline) to check alignment

### Issue 4: Borders Are Too Thick/Thin

**Symptoms:** 2px borders look like 1px or vice versa

**Fix:**
1. Select element
2. Check stroke weight in right panel
3. Set to exact pixel values: 2px or 1px
4. Set stroke position to "Inside" (exam-hall signage uses inside borders)

### Issue 5: State Switcher Appears in Import

**Symptoms:** The prototype's state switcher bar is visible

**Fix:**
1. Find the state switcher element (usually at bottom)
2. Delete it - it's just for prototype navigation
3. Real screens don't have this element

### Issue 6: Modal Overlay Doesn't Cover Screen

**Symptoms:** Modal doesn't overlay the background properly

**Fix:**
1. Modal overlay should be full viewport size
2. Check frame dimensions: should match artboard (e.g., 1440×900)
3. Background color should be rgba(20, 24, 29, .58)
4. Z-index/layer order: overlay should be on top

### Issue 7: IBM Plex Mono Tabular Numerals Don't Work

**Symptoms:** Numbers in clock/score don't align properly

**Fix:**
1. Select text with numbers
2. In Typography section (right panel)
3. Find "OpenType Features"
4. Enable "Tabular figures" (tnum)
5. Or manually set: font-variant-numeric: tabular-nums

## Handoff Preparation

Once your Figma file is complete and organized:

### 1. Create a Cover Page

Include:
- Project name: "TOEIC Practice - Exam Interface"
- Design system reference
- Key principles and rules
- Navigation guide (how to find different screens)
- Changelog (if iterating)

### 2. Add Developer Handoff Annotations

Use Figma's annotation tools:
- Mark interactive elements
- Note hover states
- Indicate keyboard shortcuts
- Document animations (skeleton pulse, etc.)
- Link to keyboard-nav.js documentation

### 3. Create a Prototype Flow

Link screens together:
1. Select first screen (exam-list)
2. Click prototype tab (right panel)
3. Click "+" on interactive element
4. Drag to target screen
5. Set interaction: Click → Navigate to
6. Set animation: Instant (exam-hall has minimal transitions)

**Key flows to prototype:**
- Exam list → Instructions → Listening → Reading → Confirm → Result
- Flag question → Navigator shows marked state
- Submit button → Confirmation modal → Result screen

### 4. Set Up Dev Mode

1. Enable Dev Mode (toggle in top toolbar)
2. Verify that:
   - CSS values export correctly
   - Colors show hex values
   - Typography shows exact px values
   - Spacing shows px (not Auto Layout units)

### 5. Share with Stakeholders

1. Click "Share" button
2. Set permissions:
   - "Can view" for reviewers
   - "Can edit" for designers
3. Copy link
4. Add to project documentation

## Plugin Alternatives

If html.to.design doesn't work well, try these:

### Plugin: Figma Import

**Pros:**
- Simpler, more straightforward
- Good for individual screens
- Free

**Cons:**
- Less sophisticated style extraction
- May need more manual cleanup

**How to use:**
1. Install plugin in Figma
2. Run plugin
3. Enter URL: http://localhost:8000/student/exam-reading.html
4. Click Import
5. Clean up as described in Post-Import section

### Plugin: Anima

**Pros:**
- Code-to-design sync
- Can update designs from code changes
- Good for iterative design

**Cons:**
- Requires Anima account
- Paid features for full functionality

### Manual Screenshot + Trace Method

If plugins fail completely:

1. Take high-resolution screenshots (Cmd/Ctrl + Shift + 4 on Mac)
2. Import screenshots into Figma
3. Lock screenshot layer (prevents accidental moves)
4. Trace over screenshot with Figma shapes
5. Extract exact colors using eyedropper
6. Measure spacing with Figma's measurement tool
7. Delete screenshot when done

This is slower but gives you perfect control.

## Resources and Documentation

### Key Files to Reference

While importing and cleaning up:
- `DESIGN.md` - Complete design system documentation
- `PRODUCT.md` - Product context and constraints
- `POLISH-SUMMARY.md` - Recent enhancements and patterns
- `prototype/assets/css/tokens.css` - All design tokens
- `prototype/assets/css/components.css` - Component styles

### Design System Checklist

After import, verify these system rules:

✅ **Colors**
- [ ] All brand colors match exact hex values
- [ ] Color styles created for all tokens
- [ ] Status washes use correct opacity/tints

✅ **Typography**
- [ ] IBM Plex fonts installed and applied
- [ ] Text styles created for all 7 levels
- [ ] Tabular numerals on clock/scores
- [ ] Letter spacing correct on condensed caps

✅ **Spacing**
- [ ] All spacing uses 4px scale
- [ ] Auto Layout gaps match tokens
- [ ] Padding matches CSS values

✅ **Components**
- [ ] Border radius = 0 everywhere
- [ ] Keylines are 2px (frame) or 1px (interior)
- [ ] Active state uses inversion (ink + white)
- [ ] Buttons have all required variants
- [ ] Options have selected/correct/wrong states

✅ **Layout**
- [ ] Desktop frames at 1440×900
- [ ] Tablet frames at 768×1024
- [ ] Mobile frames at 390×844
- [ ] Responsive behavior documented

✅ **Accessibility**
- [ ] Focus states visible
- [ ] Touch targets minimum 44×44px
- [ ] Color contrast verified
- [ ] Text readable at minimum size

## Getting Help

### If Import Fails

1. **Check browser console** while serving prototype
   - Look for CSS/font loading errors
   - Fix any broken references

2. **Try different plugins**
   - html.to.design, Figma Import, Anima
   - Each handles HTML/CSS differently

3. **Simplify the import**
   - Import one screen at a time
   - Start with simplest screen (exam-list)
   - Build up to complex ones (exam-reading)

4. **Manual recreation**
   - Use screenshot-tracing method
   - Slower but gives perfect control
   - Good fallback option

### Community Resources

- Figma Community Forum: https://forum.figma.com/
- html.to.design Support: Check plugin description for support email
- Design Systems Slack: Many designers share import tips

### Next Steps

After successful import:
1. Review DESIGN.md to understand all design rules
2. Create component library following exam-hall principles
3. Set up variants for all component states
4. Document keyboard shortcuts and interactions
5. Create prototype flows for key user journeys
6. Share with team for feedback
7. Iterate based on real device testing

---

## Quick Start Summary

1. **Serve prototype locally** (Python/Node/Live Server)
2. **Install html.to.design** plugin in Figma
3. **Import first screen:** exam-reading.html?state=success
4. **Verify colors, fonts, spacing** match DESIGN.md
5. **Create reusable components** with variants
6. **Set corner radius to 0** everywhere
7. **Organize into pages** by flow
8. **Create design system page** with all components
9. **Document rules** from exam-hall signage system
10. **Set up prototype flows** for key journeys

Good luck with your Figma import! The exam-hall signage system is distinctive and production-ready, so preserving those flat painted keylines, square corners, and inversion patterns is key to maintaining its character.
