# PersonaOS design guidelines: "Dusk"

Proposed 2026-10-30 to replace "Sky". Drawn from the owner's reference board
(`C:\Users\Purush\Desktop\PersonaOs`): the Aura voice assistant shots (dark violet, glowing orb,
big light headlines, numbers as heroes) and the Synchro / AutomatePro task tools (crisp neutral
work surfaces, hairline structure, tinted status pills, ink-black main buttons).

This file is **the bar for how PersonaOS looks**. `docs/ENGINEERING.md` is the bar for code; this one
is the bar for pixels. Both web (Angular Material 3) and phone (Flutter Material 3) follow it.

## 1. The idea

PersonaOS is two things at once: **someone you talk to** and **a place where your work lives**.
"Sky" treated both the same, so neither stood out. Dusk gives each its own register:

| Layer | Where | Feel | Borrowed from |
|---|---|---|---|
| **Presence** | Chat, voice, home greeting, empty states, reminders ringing | Dusk sky: deep violet glow, one luminous orb, large light type, few elements | Aura |
| **Workspace** | Board, backlog, planner, timeline, notes, goals, settings | Paper: neutral greys, hairlines, dense and calm, colour only for status | Synchro, AutomatePro |

One rule ties them together: **violet means the assistant**. The accent appears where the
assistant is, or where something it did is shown (focus, selection, progress, the send button,
the orb). Everything else is ink and grey. That keeps the colour meaningful instead of decorative.

The signature element is **the Orb**: a small glowing sphere that *is* the assistant. It is the
avatar in chat, the voice button, the thinking indicator (it breathes), and the centrepiece of an
empty chat. No other icon or gradient competes with it.

## 2. Colour

Tokens are semantic. Components never use raw hex; they use these roles (mapped to Material's
`--mat-sys-*` on web and `ColorScheme` on Flutter).

### Light ("Paper")

| Role | Hex | Use |
|---|---|---|
| `shell` | `#F4F4F6` | Behind the nav rail and around the working panel |
| `surface` | `#FFFFFF` | Working panel, cards, dialogs |
| `surface-sunken` | `#F7F7F8` | Table headers, input fills, inner wells |
| `surface-raised` | `#EFEFF2` | Hovered rows, selected nav item |
| `line` | `#E7E7EB` | Hairlines: borders, row dividers |
| `line-strong` | `#D4D4DA` | Input borders, dashed "add" targets |
| `ink` | `#18181B` | Text, **primary buttons (black fill, white text)** |
| `ink-muted` | `#6B6B76` | Secondary text, labels, metadata (5.3:1 on white) |
| `accent` | `#5B4FE9` | Assistant, focus ring, selection, checkboxes, progress, links (5.6:1) |
| `accent-soft` | `#EEECFD` | Selected chip, assistant tint, highlight |
| `accent-ink` | `#4338CA` | Text on `accent-soft` (6.8:1) |
| `presence` | radial `#E9E4FF` at top-centre fading to `#FFFFFF` | Light-mode Presence backdrop ("dawn") |

### Dark ("Dusk")

| Role | Hex | Use |
|---|---|---|
| `shell` | `#09080D` | Behind everything |
| `surface` | `#0D0B14` | Working panel (violet-tinted off-black, never `#000`) |
| `surface-sunken` | `#15121F` | Table headers, inputs |
| `surface-raised` | `#1C1828` | Cards, hovered rows |
| `line` | `rgba(255,255,255,0.08)` | Hairlines |
| `line-strong` | `rgba(255,255,255,0.14)` | Input borders |
| `ink` | `#F2F0F7` | Text (17:1) |
| `ink-muted` | `#9A95AB` | Secondary text (6.0:1 on cards) |
| `accent` | `#8B7CFF` | As light; **primary buttons use it with `#0D0B14` text** (6.0:1) |
| `accent-soft` | `#231C45` | Selected chip, assistant tint |
| `presence` | radial `#3B2A7A` (55% opacity) top-left + `#2A1F5C` (40%) right, over `surface` | The dusk glow behind Presence screens |

### Status (both modes)

Status is the only other colour in the Workspace. Always a **tinted pill with an icon and a
word**, never colour alone. Light text/fill pairs are at least 4.5:1, dark pairs at least 7:1.

| Status | Light text / fill | Dark text / fill | Icon |
|---|---|---|---|
| To do | `#52525B` / `#F1F1F3` | `#B4B0C2` / `#221E2E` | dashed circle |
| In progress | `#1D4ED8` / `#E8F0FE` | `#93B4FF` / `#1A2340` | half circle |
| In review | `#A15C07` / `#FDF3E1` | `#F5C46B` / `#2E2414` | eye / sparkle |
| Done | `#166534` / `#E6F6EC` | `#6FD69A` / `#132A1E` | check circle |
| Urgent / overdue | `#C2261D` / `#FDECEB` | `#FF8A80` / `#341716` | flag |
| Waiting on you | `#7E22CE` / `#F4ECFD` | `#C9A6FF` / `#2A1A40` | clock |

### The Orb

A sphere built from three radial gradients on a circle, no image:
`#8B7CFF` (core) → `#E36BD8` (lower-left bloom) → `#56C8F5` (upper-right rim), with a soft outer
glow (`box-shadow: 0 0 48px rgba(139,124,255,.45)`). Sizes: 24 (chat avatar), 40 (voice button),
120 (empty chat), 160 (voice screen). States: **idle** still, **listening** gently scales 1→1.06
with the input level, **thinking** breathes (opacity .8→1, 1.6 s), **speaking** ripples.
Under reduced motion it holds still and a text label carries the state.

## 3. Type

**Typeface: Geist** (OFL, variable 100–900; Geist Mono for numbers that line up). It reads as
the precise grotesk in the Aura shots and stays plain enough for dense tables. It replaces Plus
Jakarta Sans, whose round geometry pulled toward "friendly SaaS". Load it the way Plus Jakarta
was loaded: the web app from Google Fonts (`index.html`), the phone bundled in `assets/fonts`.

| Style | Size / line | Weight | Tracking | Use |
|---|---|---|---|---|
| Display | 44 / 48 (phone 34 / 38) | 300 | -0.03em | Presence headline: "Good evening, Purush." |
| Numeral | 56 / 56 (phone 44) | 300 | -0.04em, tabular | Stat cards: **7** tasks due |
| Headline | 24 / 30 | 500 | -0.015em | Page titles |
| Title | 16 / 22 | 600 | -0.005em | Card and section titles |
| Body | 15 / 22 (phone 16 / 24) | 400 | 0 | Chat messages, descriptions |
| Label | 13 / 18 | 500 | 0 | Buttons, chips, table cells, nav |
| Caption | 12 / 16 | 400 | 0.01em | Metadata, timestamps |
| Mono | 13 / 18 | 450 | 0, tabular | Dates in tables, IDs, durations |

Rules:
- **Light display, firm workspace.** Big Presence type is thin (300); Workspace type is never below 400.
- Headlines in sentence case, never all caps. Section labels are Title style, not tiny uppercase.
- All numbers that change or line up use tabular figures (`font-variant-numeric: tabular-nums`).
- Chat text measure stops at 68 characters; centre the column on wide screens.

## 4. Space, shape, depth

- **4-point grid.** Steps: 4, 8, 12, 16, 20, 24, 32, 48, 64.
- **Density.** Workspace rows are 40 px (table) / 36 px (compact list); Presence breathes (32–64 between groups).
- **Radius** goes from tight inside to soft outside: chip/pill 8 · input/button 10 · card 14 · panel/dialog 20 · sheet 28 (top only) · orb and avatar round.
- **Depth is drawn, not shadowed.** Workspace uses 1 px `line` borders and no shadows. Only floating
  things get shadow: menus/dialogs `0 12px 32px -8px rgba(24,24,27,.18)` (dark: `rgba(0,0,0,.6)` plus
  a 1 px `line` border). No coloured or glowing shadows except the Orb.
- **Glass only in Presence, only on dark.** Stat cards there are `rgba(255,255,255,.04)` with a
  `rgba(255,255,255,.08)` border and a 1 px inner top highlight; blur only on fixed overlays.

## 5. Layout

### Web
- **Shell:** grey `shell` frame, 248 px nav rail (collapsible to 64 px icon rail with tooltips),
  one white `surface` panel with 20 px radius and 8 px gap to the window edge (as Synchro).
- **Panel header:** breadcrumb row (48 px) over a title row with the page's one primary action on
  the right: an **ink-black** button with a leading icon ("+ New task"). Secondary actions are
  outlined icon buttons (36 px).
- **View switcher** for work modules: a segmented pill (Board · List · Timeline · Calendar) in
  `surface-sunken` with the active segment on `surface` and a hairline.
- **Nav rail:** user card at the top, the one "New chat" action as an ink button, then modules
  with 18 px icons, active item on `surface-raised`, counts as small grey badges on the right.
  Help and Settings pinned at the bottom.

### Phone
- **Home is Presence:** dusk glow, greeting in Display, the composer as a pill with the Orb send
  button, suggestion chips, then **stat cards** (horizontal scroll of 2.3 cards): due today, sprint
  progress, next reminder. Each card: title, sub-label, big Numeral, a tiny chart or waveform.
- **Bottom bar:** five targets with the voice Orb raised in the middle (Home · Board · Orb · Planner · More).
- **Work screens are Paper** even in dark mode's palette: flat lists, hairline dividers, status pills.

## 6. Components

| Component | Spec |
|---|---|
| Primary button | Light: `ink` fill, white text. Dark: `accent` fill, dark text. 40 px (phone 48), radius 10, label 13/500, optional leading icon. One per view. |
| Secondary button | `surface` fill, `line-strong` border, `ink` text. |
| Icon button | 36 × 36, `line` border, radius 10; hover `surface-raised`. Always a tooltip and an accessible name. |
| Input | `surface` fill, `line-strong` border, radius 10, 40 px; focus = 2 px `accent` ring with 2 px offset. Label above, never placeholder-only. |
| Composer | Pill, radius 24 (phone 28), `surface` on Paper / glass on Dusk; attach and mic on the left, Orb send on the right. Mode chips sit below it. |
| Chat bubble | User: `ink` fill (dark: `surface-raised`), white text, radius 18 with 6 on the tail corner. Assistant: **no bubble**, text on the canvas beside a 24 px Orb, like a document. Tool receipts are hairline cards. |
| Status pill | Height 24, radius 8, 12/500, 14 px icon + word, tinted fill per §2. |
| Priority | Outlined flag chip (Urgent red, Normal teal `#0F766E`, Low grey). |
| Table | Header row `surface-sunken`, 12/500 `ink-muted`; rows 40 px, hairline dividers, hover `surface-raised`; group headers are status pills with a count. Drag handle appears on hover. |
| Board card | `surface`, `line` border, radius 14, padding 14. Title 14/600, date chip, hairline, progress bar (4 px, `accent`), footer with avatars and counts. |
| Progress | 4 px track `line`, fill `accent`, label as Mono "55%". |
| Stat card (Presence) | Glass on Dusk / `surface` on Paper, radius 20, padding 16. One card per row may be **solid accent** to mark the most important number (Aura's "Sleep score"). |
| Dialog | Radius 20, padding 24, property list layout: label column (icon + 13/500 muted) and value column. Footer: Cancel (secondary) and Save (primary) right-aligned. |
| Empty state | Presence-style: small Orb or line illustration, one Headline sentence, one action. |
| Toast | Ink pill bottom-centre, 3–5 s, `aria-live="polite"`. |

## 7. Icons

One family, **Phosphor Regular** (1.5 px stroke), the same font files in both apps: the web app
bundles them from `@phosphor-icons/web` (a `mat-icon` names its glyph, `<mat-icon>caret-down</mat-icon>`),
the phone from `assets/fonts` through `lib/phosphor_icons.dart` (the `phosphor_flutter` package no
longer builds on current Flutter). Sizes 16 (inline), 18 (nav, buttons), 20 (toolbar), 24 (bottom bar). Filled
weight only for the active bottom-bar item. Material Icons go away.

## 8. Motion

| Token | Duration | Curve | Use |
|---|---|---|---|
| `quick` | 120 ms | `cubic-bezier(.2,0,0,1)` | Hover, press, toggles |
| `standard` | 220 ms | `cubic-bezier(.2,0,0,1)` | Menus, chips, row expand |
| `enter` | 320 ms | `cubic-bezier(.05,.7,.1,1)` | Dialogs, sheets, page content (rise 12 px + fade) |
| `exit` | 200 ms | `cubic-bezier(.3,0,1,1)` | Anything leaving |
| `breathe` | 1600 ms loop | ease-in-out | Orb thinking |

- Assistant text streams in by word with a 120 ms fade, not a typewriter caret.
- Lists stagger 30 ms per item, first 8 items only.
- Press feedback: scale 0.98 on buttons and cards.
- Transform and opacity only. `prefers-reduced-motion` (web) and `MediaQuery.disableAnimations`
  (Flutter) drop travel and loops, keep the fades.

## 9. Voice and copy

- The assistant's nickname is the only personal name the UI uses; greet with it and the time of day.
- Sentence case everywhere. Buttons say what they do ("Add task", not "Submit").
- No exclamation marks in system text. Errors say what happened and what to do.

## 10. Accessibility floor

- Text 4.5:1, large text and control borders 3:1, in **both** modes (pairs above are checked).
- Focus is always visible: 2 px `accent` ring, 2 px offset.
- Touch targets 44 px web pointer (24 minimum by WCAG, 44 by choice), 48 dp phone.
- Status never by colour alone (icon + word). Icon-only controls have names and tooltips.
- The Orb's state is also announced in text ("Listening", "Thinking").

## 11. What changes from "Sky"

| Sky (now) | Dusk |
|---|---|
| Blue `#2563EB` everywhere a colour was needed | Violet only for the assistant; ink-black main buttons in light mode |
| Plus Jakarta Sans, semibold headings | Geist: thin Presence display, firm Workspace labels, tabular numbers |
| Same pale-blue wash on chat and home | Two registers: Dusk glow for Presence, Paper for Workspace |
| Assistant replies in outlined bubbles | Assistant writes on the canvas beside the Orb |
| Material Icons | Phosphor Regular, both apps |
| Navy-grey dark theme | Violet-tinted dusk dark theme with glass stat cards |

## 12. Rollout order

1. Tokens and font (web `styles.scss`, Flutter `_personaOsScheme`), no layout change.
2. Shell and nav rail; primary button style.
3. Chat and the Orb (web and phone together).
4. Phone home with stat cards; bottom bar with the raised Orb.
5. Workspace: status pills, tables, board cards, dialogs.
6. Icons swap to Phosphor.

Each step ships on its own and keeps tests and the layout sweep green.
