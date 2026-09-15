# Sprint board — requirements

Status: agreed with the owner on 2026-09-15, being built. Progress lives in `TODO.md`.

PersonaOS brings a light version of Scrum to one person's life: work is broken down under
goals, estimated in story points, pulled into a one-week sprint on Sunday evening, and moved
across a board during the week. At the end of each sprint the user sees what they committed to
and what they finished. No burndown charts, no team features.

## 1. Concepts

| PersonaOS | Jira equivalent | Key | Lives on the board? |
|---|---|---|---|
| Goal | Epic | `GOAL-n` | No. Shown as a chip on its tasks. |
| Task | Story / task | `TASK-n` | Yes |
| Sprint | Sprint | `Sprint n` | The board shows one sprint at a time |

- **Goals stop nesting.** A goal is top level only and keeps its period (year / quarter /
  month). What used to be a sub-goal is now a task under that goal. Existing sub-goals are
  converted to tasks under their top-level goal. This also removes the period picker from the
  old "New sub-goal" form (phone testing issue #2).
- **A task may have no goal.** "Renew car insurance" is still a task.
- **Goal progress comes from its tasks:** done points ÷ estimated points. When none of its
  tasks are estimated it uses done tasks ÷ tasks; with no tasks it falls back to the manually
  set progress, as today.

## 2. Keys

- Every goal and task has a human key, `GOAL-3` or `TASK-12`, shown everywhere: board cards,
  goals page, chat receipts and proposal cards.
- **Numbers are reused.** A new item takes the lowest free number. Delete `TASK-3` and the next
  task created becomes `TASK-3`. Deletion is permanent.
  - Consequence: an old chat message that says `TASK-3` may now refer to a different task.
    Accepted for a single-user app.
  - A closed sprint's committed and completed totals are stored on the sprint when it closes,
    so deleting a finished task never rewrites history.
- **The assistant works with keys, not database ids or list positions.** Tools take and return
  `GOAL-n` / `TASK-n`. This also fixes phone testing issue #3, where the model passed a list
  position ("Goal 3 does not exist").

## 3. Story points

- Fibonacci only: **1, 2, 3, 5, 8, 13, 21**, or unestimated (`?`).
- Points express size: effort, complexity and uncertainty together — not hours.
- 13 and 21 show a "consider splitting" hint. Research on personal Scrum is consistent that
  large items are where one-person sprints fail.
- A task can be committed to a sprint while still unestimated, but the sprint header shows how
  many unestimated tasks it holds, because they make "committed points" understate the plan.

## 4. The board

Columns: **Backlog | This week | In progress | Done**.

- **Backlog** — tasks not in any sprint.
- **This week** — tasks in the viewed sprint that have not started.
- **In progress** — started. More than 3 tasks here shows a soft warning (a WIP limit is the
  single most useful Kanban habit for one person; it is a hint, never a block).
- **Done** — finished in the viewed sprint.
- Cards show the key, title, points, and the goal as a coloured chip (`GOAL-2 Learn Rust`).
- **Drag and drop** between columns and to reorder within a column, on web and phone. Every
  drag has a non-drag alternative (a "Move to" menu) for keyboard and screen-reader use, and
  because long-press drag on a phone is easy to miss.
- A sprint switcher shows **Current** and **Next**:
  - **Current** — the active sprint.
  - **Next** — pre-planning. Tasks can be added to next week's sprint at any time without a
    warning. Only the Backlog and This week columns are meaningful there.
- Tasks are created from the board (any column), from a goal, or by the assistant.

## 5. Sprint cycle (fully automatic, owner's local time zone)

A sprint runs **Sunday 20:00 → next Sunday 18:00**.

| When | What happens |
|---|---|
| **Sunday 18:00** | The active sprint **closes**. Its completed points (sum of Done) are stored. Every unfinished task (This week or In progress) **moves into the next sprint**, keeps its column, and is marked *carried over* (a counter, so a task carried three times is visible). |
| **Sunday 18:00 – 20:00** | Planning window. The next sprint is being planned; adding to it gives no warning. |
| **Sunday 19:00** | A push nudge: "Sprint *n* review: *x* of *y* points done. Time to plan sprint *n+1*." Opening it goes to the board. The assistant can run the review and planning in chat. |
| **Any time in the window** | The user can press **Start sprint** once planning is done. |
| **Sunday 20:00** | If not started yet, the next sprint **starts automatically** with whatever it holds. |
| **On start** | Committed points (sum of the sprint's task points) are frozen on the sprint. A new empty *next* sprint is created so pre-planning is always possible. |

- The first sprint starts immediately when the board is first used and ends at the coming
  Sunday 18:00 (a short first week).
- If the server was off across a boundary, the scheduler catches up on the next tick: close,
  carry over, start. A nudge more than 3 hours late is skipped rather than sent late.
- Carried-over tasks count toward the new sprint's committed points. The old sprint does not
  get credit for them. This matches common Scrum practice: velocity counts only work that is
  Done. (Scrum also suggests re-estimating remaining work; the assistant offers this during
  planning rather than forcing it.)

## 6. Scope changes during a sprint

- Adding a task to the **active** sprint (creating it there, or dragging it in from Backlog)
  asks for confirmation: "Sprint *n* has started. Adding this is a scope change." If confirmed,
  the task is marked *added mid-sprint* and its points are reported as **added**, separate from
  committed.
- Moving an unfinished task **out** of the active sprint back to Backlog asks the same way and
  is reported as **removed**.
- Re-estimating a task during the sprint is allowed and does not change the frozen committed
  number.
- The API enforces this, not only the UI: a scope change without acknowledgement is refused,
  so the assistant's tools cannot skip it either.

## 7. Sprint report

A simple list, newest first, one row per sprint:

`Sprint 12 · 13–20 Sep · committed 21 · added 3 · removed 2 · completed 18 · carried over 5`

Plus the average completed points of the last three closed sprints ("velocity"), which the
assistant uses as the default capacity when planning. No charts in this version.

## 8. Daily planner integration

- The planner stays the **daily** list; the board is the **weekly** list. Reminders stay
  separate and unchanged.
- When adding to a day, the user can **pick a task from the current sprint** (This week or In
  progress) or type a free-form item as today. A planner item linked to a task shows its key,
  and its goal chip when the task has a goal.
- Marking a planner item done does not move the task to Done: a day's work often does not
  finish a task. The user moves the card when it is finished.
- **Morning nudge:** the existing morning brief (Settings → Proactive, default 07:30) says it is
  time to plan the day and lists what is in progress on the board.

## 9. Assistant

Tools (all changes go through the existing confirmation card):

- `get_board` — current or next sprint with columns, keys, points, goals, carried-over and
  added flags; plus velocity.
- `get_sprint_report` — recent sprints' committed / added / removed / completed.
- `create_task` — title, optional goal key, optional points, destination backlog / current /
  next sprint.
- `update_task` — title, description, points, goal.
- `move_task` — to a column and/or sprint. A scope change is described on the proposal card,
  and confirming the card is the acknowledgement.
- `delete_task`.
- Goal tools switch to `GOAL-n` keys and lose the parent / link parameters.

Planning conversation guidance in the system prompt:

- **Review first:** last sprint's committed vs completed, what was carried over and why.
- **Estimate** unestimated tasks by comparing them with already-estimated ones
  (relative sizing), asking about complexity and value, and suggesting a split at 13+.
- **Capacity:** suggest committing roughly the recent velocity, and say when the plan is well
  above it. Solo capacity swings week to week (holidays, illness), so ask about the coming week
  instead of assuming it matches the last.
- **Order** by value: tasks tied to active goals and carried-over work come first.

## 10. Module and platforms

- New module **`board`**, on by default, toggleable like the others. The goals module keeps
  working without it.
- Web and mobile both get the board, the next-sprint view, the task sheet and the sprint report.

## 11. Out of scope for this version

Burndown or velocity charts, sub-tasks under tasks, multiple boards, custom columns or sprint
lengths, time tracking, and labels other than the goal chip.

## Research notes

- Personal Scrum practitioners commonly run one-week sprints starting on Saturday or Sunday,
  and warn that one person's capacity swings week to week, which is why capacity is asked for
  rather than assumed from velocity alone.
  ([Mountain Goat Software](https://www.mountaingoatsoftware.com/blog/how-i-work-and-use-scrum-personally),
  [Agile for One](https://www.getzoro.app/agile-for-one),
  [InfoQ: personal scrum](https://www.infoq.com/news/2015/02/personal-scrum))
- WIP limits help most when set by the person for themselves.
  ([Scrum.org](https://www.scrum.org/resources/blog/limiting-work-progress-wip-scrum-kanban-what-when-who-how))
- Velocity counts only Done work; unfinished items get no partial credit and ideally are
  re-estimated. Automatic carry-over can remove urgency — here the owner chose carry-over, and
  the carried-over counter plus the review nudge keep it visible.
  ([Scrum.org forum](https://www.scrum.org/forum/scrum-forum/35327/velocity-calculation-and-work-unfinished-stories),
  [Mountain Goat Software](https://www.mountaingoatsoftware.com/agile/handling-work-left-at-the-end-of-a-sprint),
  [ScrumMastered](https://scrummastered.com/blog/3-strategies-to-deal-with-sprint-carry-over/))
