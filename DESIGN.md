---
name: HeroUI Default Theme (with Signage Brand Hues)
library: "@heroui/react"
styles: "@heroui/styles"
modes:
  - light
  - dark
source: "https://github.com/heroui-inc/heroui/blob/v3/packages/styles/themes/default/variables.css"
---

# HeroUI Default Theme (with Signage Brand Hues)

Use this file as the visual implementation contract when building Extra Efficiency with HeroUI v3 (`@heroui/react` + `@heroui/styles` + Tailwind CSS v4). It adopts the HeroUI v3 Default Theme (`--radius: 0.5rem`, `shadow-surface`, `shadow-overlay`, light/dark mode semantic tokens, sentence-case headings, and compound components) while overriding the four semantic status/brand hues with the Extra Efficiency Signage palette (`#0b4ea2` Sign Blue, `#0a6b3c` Sign Green, `#f2b705` Sign Gold, `#b31217` Sign Red).

## Setup

Install `@heroui/react` and import HeroUI after Tailwind CSS, overriding the four brand hues on `:root, .light, [data-theme="light"]` and `.dark, [data-theme="dark"]`:

```css
@import "tailwindcss";
@import "@heroui/styles";

:root,
.light,
[data-theme="light"] {
  --accent: #0b4ea2;
  --accent-foreground: #ffffff;
  --success: #0a6b3c;
  --success-foreground: #ffffff;
  --warning: #f2b705;
  --warning-foreground: #14181d;
  --danger: #b31217;
  --danger-foreground: #ffffff;
  --focus: var(--accent);
}

.dark,
[data-theme="dark"] {
  --accent: #0b4ea2;
  --accent-foreground: #ffffff;
  --success: #0a6b3c;
  --success-foreground: #ffffff;
  --warning: #f2b705;
  --warning-foreground: #14181d;
  --danger: #b31217;
  --danger-foreground: #ffffff;
  --focus: var(--accent);
}
```

Apply the semantic canvas colors at the application root:

```tsx
<body className="bg-background text-foreground">{children}</body>
```

Use `class="light"` or `data-theme="light"` for light mode and `class="dark"` or `data-theme="dark"` for dark mode. The `default` theme is the light theme.

## Design Direction

- Build calm, modern interfaces with a clear content hierarchy.
- Prefer whitespace, typography, and surface levels over decoration.
- Use one strong accent (`--accent: #0b4ea2`) for primary actions and reserve status colors for meaning.
- Compose interfaces from HeroUI components before creating custom primitives.
- Keep interactions accessible, keyboard friendly, and visibly focused.
- Let semantic tokens adapt the interface between light and dark modes.

## Colors

Use semantic HeroUI tokens and Tailwind utilities in product code. Raw values are included for design handoff and visual QA only. Do not branch component code by color mode.

| Role | Tailwind utilities | CSS variables | Light | Dark | Purpose |
| --- | --- | --- | --- | --- | --- |
| Canvas | `bg-background text-foreground` | `--background`, `--foreground` | `oklch(0.9702 0 0)`, `oklch(0.2103 0.0059 285.89)` | `oklch(12% 0.005 285.823)`, `oklch(0.9911 0 0)` | Page background and primary content |
| Surface | `bg-surface text-surface-foreground` | `--surface`, `--surface-foreground` | white, foreground | `oklch(0.2103 0.0059 285.89)`, foreground | Cards and non-floating containers |
| Secondary surface | `bg-surface-secondary` | `--surface-secondary` | `oklch(0.9524 0.0013 286.37)` | `oklch(0.257 0.0037 286.14)` | Nested or lower-emphasis regions |
| Tertiary surface | `bg-surface-tertiary` | `--surface-tertiary` | `oklch(0.9373 0.0013 286.37)` | `oklch(0.2721 0.0024 247.91)` | Stronger surface separation |
| Overlay | `bg-overlay text-overlay-foreground` | `--overlay`, `--overlay-foreground` | white, foreground | `oklch(0.2103 0.0059 285.89)`, foreground | Popovers, menus, dialogs, and tooltips |
| Backdrop | `bg-backdrop` | `--backdrop` | black at 50% | black at 60% | Content dimming behind overlays |
| Muted content | `text-muted` | `--muted` | `oklch(0.5517 0.0138 285.94)` | `oklch(70.5% 0.015 286.067)` | Supporting text and secondary icons |
| Neutral action | `bg-default text-default-foreground` | `--default`, `--default-foreground` | `oklch(94% 0.001 286.375)`, eclipse | `oklch(27.4% 0.006 286.033)`, snow | Neutral controls and selected states |
| Accent (Sign Blue) | `bg-accent text-accent-foreground` | `--accent`, `--accent-foreground` | `#0b4ea2`, `#ffffff` | same as light | Primary actions, focus, reached CEFR band, and emphasis |
| Success (Sign Green) | `bg-success text-success-foreground` | `--success`, `--success-foreground` | `#0a6b3c`, `#ffffff` | same as light | Positive outcomes, open conditions, and correct answers |
| Warning (Sign Gold) | `bg-warning text-warning-foreground` | `--warning`, `--warning-foreground` | `#f2b705`, `#14181d` | same as light | Caution, flagged questions, text highlights, and attention |
| Danger (Sign Red) | `bg-danger text-danger-foreground` | `--danger`, `--danger-foreground` | `#b31217`, `#ffffff` | same as light | Errors, refusal, negative net revenue, and destructive actions |
| Field | `bg-field text-field-foreground` | `--field-background`, `--field-foreground` | white, eclipse | eclipse, foreground | Inputs and interactive fields |
| Border | `border-border` | `--border` | `oklch(90% 0.004 286.32)` | `oklch(28% 0.006 286.033)` | Component boundaries |
| Separator | `border-separator` | `--separator` | `oklch(92% 0.004 286.32)` | `oklch(25% 0.006 286.033)` | Quiet structural dividers |

Hover, focus, soft, inverse, and secondary or tertiary border colors are derived with `color-mix()`. Use utilities such as `bg-accent-hover`, `bg-accent-soft`, `text-accent-soft-foreground`, `border-border-secondary`, and `border-separator-secondary` instead of recreating those formulas.

## Typography

HeroUI inherits the application's font family (`IBM Plex Sans` for UI/body, `IBM Plex Sans Condensed` for display titles/badges, and `IBM Plex Mono` for timers, scores, and tabular figures) and uses Tailwind's type scale. Preserve the component defaults unless the view requires tabular numeric alignment.

- Use concise, descriptive headings in sentence case.
- Use `text-foreground` for primary content and `text-muted` for supporting content.
- Keep body copy readable with a comfortable line height and constrained line length.
- Use tabular numbers (`tabular-nums` / `font-mono`) for changing metrics, timers, prices, counters, and aligned numeric data.
- Avoid all-caps labels except for short, established conventions (e.g., CEFR levels `A1`–`C2`, certificate codes `TOEIC`, `IELTS`, `VSTEP`).

## Layout

The spacing base is `--spacing: 0.25rem`, giving the system a 4px rhythm through Tailwind spacing utilities.

- Use consistent Tailwind gaps and padding rather than one-off values.
- Keep related controls and content visually grouped.
- Constrain wide reading layouts and size containers to their content.
- Avoid doubling parent and child padding in the same direction.
- Align sibling cards, controls, labels, and metrics to shared visual anchors.

## Shapes

The base radius is `--radius: 0.5rem` (8px). The default field radius is `--field-radius: calc(var(--radius) * 1.5)` (12px).

| Utility | Formula | Default value |
| --- | --- | --- |
| `rounded-xs` | `calc(var(--radius) * 0.25)` | 2px |
| `rounded-sm` | `calc(var(--radius) * 0.5)` | 4px |
| `rounded-md` | `calc(var(--radius) * 0.75)` | 6px |
| `rounded-lg` | `calc(var(--radius) * 1)` | 8px |
| `rounded-xl` | `calc(var(--radius) * 1.5)` | 12px |
| `rounded-2xl` | `calc(var(--radius) * 2)` | 16px |
| `rounded-3xl` | `calc(var(--radius) * 3)` | 24px |
| `rounded-4xl` | `calc(var(--radius) * 4)` | 32px |

Use the scale consistently. Inner media and nested surfaces should have a visibly related, usually smaller, radius than their parent.

## Elevation and Depth

- Use `shadow-surface` for cards and raised page content.
- Use `shadow-overlay` for floating layers such as dialogs, menus, and popovers.
- Use `shadow-field` for form controls when the component applies it.
- Dark mode intentionally removes surface and field shadows and gives overlays a subtle inset edge.
- Do not stack custom shadows on HeroUI components that already provide elevation.

## Components

- **Buttons:** Use semantic variants. `primary` is the main action, `secondary` is an alternative, `tertiary` is dismissive or low emphasis, `outline` is a bordered low-emphasis action, `ghost` is an unbordered low-emphasis action, `danger` is destructive, and `danger-soft` is a quieter destructive action. Prefer one primary action per context and reach for an existing variant instead of restyling a button.
- **Cards and surfaces:** Use `Card`, `Surface`, and semantic surface utilities. Create hierarchy with surface levels and spacing before adding borders.
- **Forms:** Use HeroUI field components (`Form`, `TextField`, `TextArea`, `NumberField`, `Select`, `RadioGroup`, `CheckboxGroup`, `Switch`, `Slider`, `SearchField`, `Label`, `FieldError`, `Description`) so background, foreground, placeholder, border, focus, radius, validation, and disabled states stay consistent.
- **Status:** Use `success`, `warning`, and `danger` (`Chip`, `Alert`, `Badge`) only for their semantic meanings. Pair color with text or an accessible label.
- **Overlays:** Use HeroUI `Modal`, `AlertDialog`, `Drawer`, `Popover`, `Dropdown`, `Toast`, and `Tooltip` components so focus management, dismissal, and keyboard behavior remain accessible.
- **Compound components:** Compose exported parts (`Card.Header`, `Card.Content`, `Tabs.List`, `Tabs.Tab`, `Tabs.Panel`, `Modal.Backdrop`, `Modal.Container`, `Modal.Dialog`, etc.) instead of replacing component behavior with custom divs. Preserve labels, roles, and keyboard interactions.

## Interaction

- Use HeroUI and React Aria states, including `data-hovered`, `data-pressed`, `data-focus-visible`, `data-disabled`, and `data-invalid`.
- Every interactive element needs a visible focus indicator. The default focus color is the accent, with a 2px ring offset.
- The default disabled opacity is `0.5`; disabled controls use a not-allowed cursor.
- Use motion to clarify state changes, not as decoration. Respect reduced-motion preferences. Never replay entrance animations on client-side question or tab transitions.
- Keep icon-only and touch controls large enough to target and give them accessible labels.

## Do

- Use HeroUI components and their documented variants before writing custom primitives.
- Consult `./.heroui-docs/react` and the `heroui-react` MCP server (`list_components`, `get_component_docs`, `get_theme_variables`) before using any HeroUI v3 component.
- Use semantic tokens as implementation handles and raw values only for reference or migration.
- Use the same token in light and dark modes and let CSS resolve the current value.
- Establish hierarchy with content order, spacing, type, and surface level.
- Reserve accent for meaningful emphasis and keep status colors semantic.
- Use separators sparingly; prefer spacing for ordinary grouping.
- Keep active, selected, loading, invalid, and disabled states visible and accessible.

## Don't

- Don't hardcode colors, shadows, radii, or spacing when a HeroUI token or Tailwind utility exists.
- Don't use raw neutral palette classes such as `gray-*` where a semantic surface or content token applies.
- Don't use color alone to communicate state or hierarchy.
- Don't mix unrelated radius, padding, or elevation systems in one view.
- Don't make static content look interactive with hover or pressed effects.
- Don't overuse cards, borders, badges, icons, accent color, or nested heavy surfaces.
- Don't bypass HeroUI interaction behavior with inaccessible custom markup.
- Don't use HeroUI v2 flat props (`onValueChange`, `ModalContent`, `CardBody`); always use HeroUI v3 compound components (`onChange`, `Modal.Dialog`, `Card.Content`).

## Source of Truth

- Local HeroUI v3 documentation & demos: `./.heroui-docs/react`
- HeroUI v3 MCP server: `heroui-react` (`list_components`, `get_component_docs`, `get_component_source_code`, `get_component_source_styles`, `get_theme_variables`, `get_docs`)
- Default theme variables: https://github.com/heroui-inc/heroui/blob/v3/packages/styles/themes/default/variables.css
- Tailwind token mappings: https://github.com/heroui-inc/heroui/blob/v3/packages/styles/themes/shared/theme.css
- Component documentation: https://heroui.com/docs/react/components

When this file and the current package disagree, follow the installed HeroUI version and its theme CSS.
