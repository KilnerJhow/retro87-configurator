---
name: Retro87
description: Bench configurator for the 8BitDo Retro 87 / 87 X — deep slate in three levels, cool text, and a single amber accent
colors:
  background: "#0F1219"
  surface: "#161A23"
  surface-2: "#1C212C"
  line: "#262C39"
  line-soft: "#1F2531"
  foreground: "#E7EAF2"
  foreground-dim: "#A6AEC2"
  foreground-muted: "#8791A6"
  accent: "#E9B04C"
  accent-hover: "#F2C066"
  accent-soft: "#3A2F16"
  on-accent: "#14171E"
  signal-ok: "#63D3A2"
  signal-danger: "#E4736A"
typography:
  headline:
    fontFamily: "Inter, Segoe UI, sans-serif"
    fontSize: "20px"
    fontWeight: 600
    lineHeight: 1.2
  title:
    fontFamily: "Inter, Segoe UI, sans-serif"
    fontSize: "17px"
    fontWeight: 600
    lineHeight: 1.25
  body:
    fontFamily: "Inter, Segoe UI, sans-serif"
    fontSize: "13px"
    fontWeight: 400
    lineHeight: 1.45
  label:
    fontFamily: "Inter, Segoe UI, sans-serif"
    fontSize: "11px"
    fontWeight: 600
    lineHeight: 1.2
  key:
    fontFamily: "Consolas, DejaVu Sans Mono, Menlo, monospace"
    fontSize: "13px"
    fontWeight: 600
rounded:
  sm: "6px"
  md: "8px"
  lg: "10px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "18px"
components:
  button-primary:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.on-accent}"
    rounded: "{rounded.md}"
    padding: "9px 16px"
  button-primary-hover:
    backgroundColor: "{colors.accent-hover}"
  button:
    backgroundColor: "{colors.surface-2}"
    textColor: "{colors.foreground}"
    rounded: "{rounded.md}"
    padding: "8px 12px"
  button-ghost:
    textColor: "{colors.foreground-dim}"
    rounded: "{rounded.md}"
    padding: "7px 10px"
  button-icon:
    textColor: "{colors.foreground-dim}"
    rounded: "{rounded.md}"
    size: "28px"
  lock-armed:
    backgroundColor: "{colors.accent-soft}"
    textColor: "{colors.accent}"
    rounded: "{rounded.md}"
    padding: "5px 11px"
  lock-locked:
    textColor: "{colors.foreground-dim}"
    rounded: "{rounded.md}"
    padding: "5px 11px"
  input:
    backgroundColor: "{colors.surface-2}"
    textColor: "{colors.foreground}"
    rounded: "{rounded.md}"
    padding: "7px 9px"
  card:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.lg}"
    padding: "14px"
  list-row:
    textColor: "{colors.foreground}"
    padding: "5px 8px"
  tab:
    textColor: "{colors.foreground-muted}"
    padding: "9px 14px"
---

# Design System: Retro87

## Overview

**Creative North Star: "The Calibration Bench"**

A quiet, matte calibration bench. The keyboard is the subject, and the interface steps back:
no surface shines, no gradient competes, no ornament demands attention. The foundation is dark
slate in three levels of tactile elevation, with cool, almost silver text; above it, **a single
amber** acts like an instrument dial — lighting up only for intent (the primary action) or state
(writing enabled, a modified key, a warning).

The density belongs to a tool, not a decorative dashboard: the drawn keyboard (85 keys) and
8 macro slots must be easy to scan, so keys are solid and aligned to real geometry, labels
are short and uppercase, and each group lives in a card clearly defined by a 1 px line —
never a shadow. State speaks louder: a locked padlock is subtle, an unlocked one has a muted
amber background, and the status bar changes glyph and color according to what just happened.

Confirmed visual rejection: the official 8BitDo program (dark, glossy, "gamer"). No reflections,
glass, gradients, or second accent belong here — this identity is ours and the opposite of that.

**The only idea borrowed from the official program: the callout.** No shape, color, or shine is
copied; the borrowed idea is a gesture observed on the official app's mapping screen: the
keyboard's connection panel is drawn beside the board, the group being configured gets a
**dashed** outline and a **leader line** connecting it to a one-line caption, and the same frame
moves when the step changes (*Select the interface* → *Select the Super Button to configure*).
This became **The Callout** (see Components):
a 1 px dashed frame in `colors.foreground-muted`, a 26 px leader line, and an 11 px caption,
only one per screen, pointing to the next click. This is the only overlap between the official
app and this system — the pointing gesture, not the appearance.

**Key Characteristics:**

- Deep slate in three levels; depth only through tone and a 1 px line.
- A single amber accent, reserved for intent and state.
- Cool text in three levels (paper, silver, mist) — no warm gray.
- Uppercase 11 px labels; monospace only for keys, ids, and paths.
- Green = success, red = failure, amber = warning or permission; never interchangeable.
- Restrained, precise controls; radius 6–10, 1 px border, zero shadow.
- One dashed callout per screen: point to the current step instead of explaining it.

## Colors

Bench palette: a slate foundation in three levels, cool text in three levels, and an amber
accent that means intent or state.

### Primary

- **Instrument Amber** (`colors.accent`): the primary intentional action (the enabled Write to
  keyboard button), unlocked padlock title, modified-key marker, and active-tab underline.
  The system's only accent.
- **Lit Amber** (`colors.accent-hover`): primary button hover response.
- **Muted Amber** (`colors.accent-soft`): unlocked padlock background — the only allowed solid
  amber block.
- **Ink on Amber** (`colors.on-accent`): text and icon over solid amber.

### Neutral

- **Deep Slate** (`colors.background`): window foundation and action/tab strips.
- **Raised Graphite** (`colors.surface`): cards, header, and status bar — the first level above
  the foundation.
- **Bench Graphite** (`colors.surface-2`): the second level: fields, combos, note wells, and the
  key table header.
- **Slate Line** (`colors.line`): 1 px border on cards, fields, and controls.
- **Muted Line** (`colors.line-soft`): internal divider between table rows; deliberately subtler
  than the card border.
- **Cool Paper** (`colors.foreground`): primary text and numeric values.
- **Matte Silver** (`colors.foreground-dim`): hints inside cards, control labels, ghost buttons.
- **Mist Gray** (`colors.foreground-muted`): metadata (key position, summary, directory path,
  empty-state subtitles) and callout color — dashed frame, leader, and caption.
  Corrected to **5.6:1** on `colors.surface` and 6.0:1 on `colors.background`, meeting AA at 11–12 px.
- **Signal Green** (`colors.signal-ok`): success — "Configuration read", "written", firmware up to date.
- **Signal Red** (`colors.signal-danger`): failure — read, write, backup, or verification.
  Warnings (`Warn` in code) use **Instrument Amber** itself.

### Named Rules

**The Single Voice Rule.** Amber marks intent or state, never decoration: primary action,
writing enabled, and modified key. It appears nowhere else — its rarity makes the unlocked
padlock readable from a distance.

**The Signal Rule.** Green only for success, red only for failure, amber for warning and
permission. An error must never look like "configuration read".

## Typography

**Display Font:** Inter (with Segoe UI, sans-serif; bundled through `Avalonia.Fonts.Inter` and
registered with `WithInterFont()` in `Program.cs` — no `.ttf` in the repository)
**Body Font:** Inter (with Segoe UI, sans-serif)
**Label/Mono Font:** Consolas (with DejaVu Sans Mono, Menlo, monospace)

**Character:** a single neutral family carries the whole interface — its voice comes from size
and weight, not changing fonts. Monospace is a measuring tool, never a "technical" costume:
it exists for key names, protocol ids (`0x5C9`), and file paths.

### Hierarchy

- **Headline** (600, 20 px, 1.2): tab title ("Keys", "Macros", "Keyboard settings").
- **Title** (600, 17 px, 1.25): product name in the header.
- **Body** (400, 13 px, 1.45): running text, field values, key names — the window's default body.
- **Label** (600, 11 px, uppercase): section label inside a card ("CONNECTION", "KEYBOARD PROFILE"),
  table column header, and target category.
- **Key / Mono** (600, 13 px, monospace): key name in the table, position, ids, and paths.
- **Hint** (400, 12 px, `colors.foreground-dim`): instruction below a control; never larger than
  the text it explains.

### Named Rules

**The Instrument Label Rule.** A section label is always 11 px, 600, uppercase, and
`colors.foreground-muted`; it names the part and never competes with the value. If the label
needs to grow to be readable, the hierarchy is wrong, not the size.

**The Mono Rule.** Monospace is measurement: only keys, ids, paths, and protocol values.
Running text in monospace is costume and does not belong here.

## Layout

Fixed desktop window, no breakpoints: default 1000×700, minimum 880×600 (below that, content
breaks rather than adapting). The structure has four stacked strips, always in the same order:

1. **Header** (padding 18×14): product name on the left; with a keyboard connected, its name and
   *Switch keyboard* on the right. Disconnected, the window opens on the **keyboard list**:
   list on the left (340 px), selected device details and *Connect* on the right; connecting
   automatically reads the keyboard.
2. **Action bar** (padding 18×10): `Read from keyboard` and, only when the screen differs from
   the keyboard, `Save` (amber primary) with the change count; *Check for updates* isolated on
   the right. `Save` opens a dialog with the list (item, on the keyboard, after saving);
   confirming grants permission to write.
3. **Tabs** (5: Mapping, Macros, Settings, Profiles, About the keyboard) with content in cards.
4. **Status bar** (padding 18×9): severity glyph + message, and a secondary firmware line.

Spacing rhythm: **4 / 8 / 12 / 18 px** (`xs`–`lg`). Between cards: 12 px; content side gutters:
8–10 px; inside cards: 14 px; between a label and its first control: 6 px; between sibling controls:
8–10 px. Always more space above a section label than below it.

Density: a drawn key has a unit of up to 46 px per side, calculated from the board's allocated
width — never above 46 or below 24 — without `Viewbox`, so captions do not shrink with it;
the function row is separated from letters by 0.25 units, and the key gap is 2 px. The Super
Button block is a card with 10 px padding and five connection chips ~40 px high; below them,
the detail area holds the active connection's two buttons, 140 px each. Macro slots are
stacked cards with 14 px padding and 10 px gaps. The action bar does not grow: it is a strip
with 5 px + 5 px around the padlock.

**Scrolling.** Scrolling is part of the surface and always visible: no auto-hide, thumb in
`colors.line` and `colors.foreground-muted` on hover, avoiding the inherited interface's 2 px
thread. Content leaves a 14 px gap on the right, so the scrollbar does not paint over card borders.

**Minimum width.** At 880×600 (the window minimum), the board shrinks with it — the calculated
unit drops to a 24 px floor, with fixed-size captions. There is no side panel: the key editor
is a popover over the board, so nothing competes for width. The Super Button block sits below
the board in the same scroll area, always visible. The tab summary sits on the title's line,
on the right: this keeps the whole board visible at 700 px height.

**Languages.** The UI is pt/en (`Localization/Strings.cs`, `{loc:T key}` in XAML). The selector
is in the header, and a change only takes effect after restarting, with a notice: live changes
did not update text calculated in the ViewModel.

## Elevation & Depth

**Flat through tone, no shadows.** Depth is position on the ramp, not physical distance:
`colors.background` is the foundation, `colors.surface` the first level (cards, strips), and
`colors.surface-2` the second (fields, note wells, table header). Each level transition is
reinforced by a 1 px line (`colors.line` for boxes, `colors.line-soft` for internal dividers).
The app's only shadows are those Fluent adds to combo/menu popups — an operating-system
exception, not part of this design system.

### Named Rules

**The Flat Bench Rule.** If an element seems to need a shadow, it needs a tonal level or a line.
No `box-shadow` is authored in this project.

## Shapes

Restrained, predictable shapes: no pills, no sharp corners.

- **Radius:** `lg` 10 px for cards; `md` 8 px for controls (button, field, combo, padlock);
  `sm` 6 px for note wells; table rows have no radius (the table is one continuous rectangle).
- **Borders:** always 1 px; cards use `colors.line`, internal dividers use `colors.line-soft`.
  A 2 px border on only one side is prohibited — it shouts louder than amber.
- **Rules:** the only lines without an outline are the 1×30 px vertical separator between
  `Read from keyboard` and the lock, and the 2 px amber underline beneath the active tab
  (a position marker, not a box border).
- **The callout:** 1 px dashed frame (dash 3,3; radius 9) in `colors.foreground-muted`, drawn
  6 px outside the group it points to, plus a 26 px dashed leader and an 11 px caption in the
  same color. It is **not a box**: no background, no shadow, never amber, never 2 px, and never
  more than one per screen — it marks the next click and leaves when the step ends.
- **Icons:** drawn as `Path`/SVG with the same stroke (16 px, solid outline, no font glyph).

## Components

### Buttons

- **Shape:** 8 px radius, 12×8 padding; primary grows to 16×9 and weight 600.
- **Primary:** `colors.accent` background, `colors.on-accent` text, no border. Appears **only**
  when the intentional action is enabled (Write to keyboard with the lock open); when locked,
  the same button is an ordinary disabled button.
- **Hover / Focus:** primary hover lightens to `colors.accent-hover`; other buttons use
  `colors.line` as their hover background; the focus ring comes from Fluent (not custom).
- **Disabled:** `colors.surface` background and 55% opacity — readable without looking broken.
- **Ghost:** no background or border at rest, `colors.foreground-dim` text, for tertiary actions
  ("Refresh", "Check for updates", "Refresh list").
- **Icon:** 28×28 without background, drawn glyph, `colors.foreground-dim`; only row actions
  (restore key, move/remove macro step), always with an accessible name and tooltip.

### Inputs / Fields

- **Style:** `colors.surface-2` background, 1 px `colors.line` border, 8 px radius, 7×9 padding,
  `colors.foreground` text; combo with the target category in 11 px `colors.foreground-muted`
  aligned to the right of the name.
- **Focus:** Fluent ring over the border; no custom glow.
- **Disabled:** same language as disabled buttons (`colors.surface` background, 55%).

### Cards / Containers

- **Corner Style:** 10 px.
- **Background:** `colors.surface` over `colors.background`.
- **Border:** 1 px `colors.line` all around.
- **Shadow Strategy:** none (see Elevation).
- **Internal Padding:** 14 px; table cards use 0, leaving the `colors.surface-2` header at the top.
- **Well:** `colors.surface-2` block with a 1 px border and 6 px radius, for notes, guidance, and
  empty states. A well has no header of its own and does **not** nest inside another well.

### Navigation

- **Navigation:** 5 items, 13 px `colors.foreground-muted` text, 9×14 padding; the active tab
  becomes `colors.foreground` and gains a 2 px amber underline beneath the label. Order is fixed
  (Mapping, Macros, Settings, Profiles, About the keyboard); Mapping is first and initially open.
  No tab is hidden by state — only content changes, showing a guidance well when no data exists.
- **Shortcuts:** F5 refreshes the keyboard list, Ctrl+L reads from the keyboard, Ctrl+S saves the
  local profile, and Alt+1..5 switch tabs. Each appears in its control's tooltip and UI Automation
  `AcceleratorKey`, so screen readers announce the shortcut with the action.

### Signature Components

**Write lock.** The app's most important control: a two-line `ToggleButton`, with a drawn
14×16 px padlock, 12 px 600 title, and 11 px hint. Locked: transparent background, `colors.line`
border, `colors.foreground-dim` text, "click to enable" hint. Unlocked: `colors.accent-soft`
background, `colors.accent` border, title and padlock in `colors.accent`, "click to lock again"
hint. Disabled (no writable session): title and hint in `colors.foreground-muted`, explaining
the reason instead of the action. It sits **between** `Read from keyboard` and `Write to keyboard`,
in the order the user should act, and permission applies only to the current connection.

**Drawn keyboard (Mapping).** The tab no longer has a table: the board's 85 keys are clickable
rectangles positioned using the device's real geometry (`Models/KeyboardGeometry.cs` +
`Views/KeyboardPanel.cs`), in `colors.surface-2` with a `colors.line` outline and monospace labels.
States: **normal** (Bench Graphite + Line), **modified** (only the `colors.accent` outline; no dot,
which interfered with the letter), **selected** (`colors.accent-soft` background +
`colors.accent` outline), and hover (`colors.line` background). The focus ring comes from the
system — amber, because it uses the theme accent. The board scales through a unit calculated
from its allocated width (`KeyboardPanel.Unit`, ceiling 46 px, floor 24), so the same geometry
works at any window width with fixed-size captions. Selecting a key opens a **popover** beside
it (`Popup` with `PlaceInsideWindow`: below or above depending on space, with lateral adjustment
at edges), with name, position, target selector, and "Restore the default".
Content is the same everywhere: `Views/SelectedKeyPanel.axaml`.

**Super Button, in two steps.** The ten buttons are **not** board keys: they live in a card below
it; on the device, they are ten `keyCode` `0xF3` records addressed by index (see
`docs/PROTOCOL.md` §21). The card presents them **behind the connections**: two on the keyboard
itself (`KA`/`KB`) and eight on the four connections (**A**, **B**, **X**, **Y**), two per
connection — the screen calls a *connection* what the model and protocol call an *interface*.
The block is read as two steps, in the order the user acts:

1. **The connections.** Five chips ("On the keyboard", A, B, X, Y): name in 11 px 600, and the
   pair's wire names in 10 px monospace (`K1 · K2`). The active connection chip rises one level
   (`colors.surface-2` + `colors.line`), and its name becomes `colors.foreground`; others stay
   transparent with `colors.line-soft` and `colors.foreground-muted` names.
2. **The detail area.** Below, the active connection's two Super Buttons, 140 px each: 12 px 600
   name above the current target in 10 px. The same key button at a readable size — normal,
   **modified** (amber outline + 5 px dot), and **selected** (muted amber, with the target line
   raised to `colors.foreground-dim`, because mist over muted amber gives 4.2:1 and fails AA at 10 px).

**The detail area exists only after a choice.** Until a connection is selected, the block is
just the five-chip row — no "default" pair that might be mistaken for an existing choice.
**The Callout** (see Shapes) marks only the current step: without a selected connection, the
dashed frame surrounds the chips and says "1. Pick a connection."; after selection, the detail
area appears, the frame moves to it, and the caption says "2. Pick the Super Button of connection A."
The sentence names the connection so the pair is never read out of context. Once a button is
selected, **the frame withdraws**: the amber button is already the answer, and a frame pointing
at a completed choice would be noise. It returns if the side panel leaves the pair (another key,
or a re-read). Choosing a connection does not select any button: if the side panel was showing a
Super Button from another connection, it closes because the visible pair is different.
The 5 px amber dot also summarizes the pair on the chip (one modified button is enough), restoring
the at-a-glance readability of ten buttons side by side. A **re-read** returns to step 1.
Their "modified" marker — and restore button, labeled "Back to the value read" instead of
"Restore the default" — references the value read from the device because these ten have no
factory table.

**Macro slot.** Two states in the same card. **Empty**: one line — "Slot N" in monospace 600,
"empty" in 12 px `colors.foreground-muted`, and a small "Create macro" button on the right;
seven or eight slots are usually in this state, making them cheap to scan without showing
"cycles 0" for a nonexistent macro. **Open**: "Slot N" + name + cycles + interval (with a
tooltip explaining 255 = forever) + the three step buttons + step list; a still-empty slot
gets a "Collapse" ghost button to close the editor again. A slot with a name or steps read
from the device opens automatically.

**App icon.** The slate plate (`colors.background`), key (`colors.surface-2`), and amber line
(`colors.accent`) fused into its base — the same gesture as the active tab, without text,
gradient, or shadow. Drawn in code using the SkiaSharp already loaded by the app (`Brand/AppIcon.cs`)
and exported by `Retro87 --make-icon`: 16, 32, 48, and 256 px PNGs inside an `.ico` (referenced
by the project's `<ApplicationIcon>`).

**Status bar.** Drawn mark (10 px `Path`, never a font glyph) + message, with severity reflected
in both mark and text color: `●` neutral amber (`colors.foreground-muted`) for information,
`✓` `colors.signal-ok` for success, `!` `colors.accent` for warning/refusal, `✕`
`colors.signal-danger` for failure. The firmware hint lives on a second line, at 12 px,
in the same severity color. It is the app's only error channel: the message names the problem
**and** the next step and remains readable until the next action.

## Do's and Don'ts

### Do:

- **Do** use amber only for the Single Voice Rule's three roles: primary action, writing enabled,
  and modified key.
- **Do** create depth through `background → surface → surface-2` plus a 1 px line.
- **Do** keep section labels at 11 px 600 uppercase `colors.foreground-muted`, with more space
  above than below.
- **Do** reserve monospace for keys, ids, and paths.
- **Do** use `colors.signal-ok` only for success and `colors.signal-danger` only for failure.
- **Do** give every icon button an accessible name (UI Automation) and tooltip.
- **Do** keep all text at AA: `colors.foreground-muted` on `colors.surface` is 5.6:1.
- **Do** write status messages as problem + next step.
- **Do** use the callout to point to the current step — one per screen, in
  `colors.foreground-muted`, with a caption naming what is being selected.

### Don't:

- **Don't** reintroduce `#6D7689` (the old muted shade, 3.8:1 on `colors.surface`) for normal text.
- **Don't** use Unicode font glyphs as icons (⟲, ↑, ↓, ✕): icons are drawn `Path` elements
  with the same stroke.
- **Don't** color a card, well, or notice's left border at 2 px — one-sided borders are prohibited.
- **Don't** use shadows, gradients, glass, or a second accent on any surface.
- **Don't** copy the official app's glossy, "gamer" language; this identity is its opposite.
  An idea (the callout) may be borrowed, never a shape.
- **Don't** turn the callout into a box: no background, no 2 px border, no amber, and never two
  frames competing for attention.
- **Don't** paint the Write to keyboard button amber while locked — primary styling signals
  that writing is allowed.
- **Don't** nest wells inside wells or cards inside cards; the second level is content, not a container.
