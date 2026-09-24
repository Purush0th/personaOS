# Sprint board — requirements

Status: agreed with the owner on 2026-09-15, being built. Progress lives in `TODO.md`.

PersonaOS brings a light version of Scrum to one person's life: work is broken down under
goals, estimated in value points, pulled into a one-week sprint on Sunday evening, and moved
across a board during the week. At the end of each sprint the user sees what they committed to
and what they finished. No team features.

Revised 2026-09-16: sprints are now created, started and completed by hand (Jira's flow, not a
timer), the backlog has its own page, tasks / goals / sprints each have a page of their own with
comments and attachments, and "story points" are called **value points**.

## 1. Concepts

| PersonaOS | Jira equivalent | Key | Lives on the board? |
|---|---|---|---|
| Goal | Epic | `GOAL-n` | No. Shown as a chip on its tasks. |
| Task | Story / task | `TASK-n` | Yes |
| Sprint | Sprint | `Sprint n` | The board shows one sprint at a time |

- **Goals stop nesting.** A goal is top level only and keeps its period (year / quarter /
  month). What used to be a sub-goal is now a task under that goal. Since 2026-09-22 a goal
  also has a start and an end date: any start, future included; a month goal runs at most 31
  days, a quarter at most 90, and a year ends on 31 December of its start year. Existing sub-goals are
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

- Sprints have keys too, `SPRINT-2`, plus an optional name ("Paperwork week") and start and end
  dates the user sets. Sprint numbers are reused the same way when a *planned* sprint is deleted.

## 3. Value points

- Fibonacci only: **1, 2, 3, 5, 8, 13, 21**, or unestimated (`?`).
- Points express size: effort, complexity and uncertainty together — not hours.
- 13 and 21 show a "consider splitting" hint. Research on personal Scrum is consistent that
  large items are where one-person sprints fail.
- A task can be committed to a sprint while still unestimated, but the sprint header shows how
  many unestimated tasks it holds, because they make "committed points" understate the plan.

## 4. Two views of the board: Sprint and Backlog

The top navigation has one **Board** entry. Inside it a **Sprint | Backlog** switch moves between
the two views, at `/board` and `/board/backlog`.

**Sprint** is the sprint that is running, and nothing else: **To do | In progress | Done**.

- **In progress** — more than 3 tasks here shows a soft warning (a WIP limit is the single most
  useful Kanban habit for one person; it is a hint, never a block).
- Cards show the key, title, points, the goal as a coloured chip (`GOAL-2 Learn Rust`), and small
  badges for carried-over, added mid-sprint, comments and attachments.
- With nothing running, the board says so and points at the Backlog view.

**Backlog** is the plan, and reads like a Jira backlog: the running sprint at the top, every
sprint planned after it, then the backlog itself at the bottom.

- Each sprint section shows its key, name, dates, issue count and points, and carries its own
  **Start sprint** / **Complete sprint** / Edit / Delete buttons.
- Rows show status, priority and points inline, so planning is one page of edits.
- **Drag and drop** moves work between sprints and the backlog, and reorders within a group.
  Every drag has a "Move…" menu alternative, for keyboard use and because long-press drag on a
  phone is easy to miss.
- Tasks are created in any group, from a goal, or by the assistant.

## 5. Sprint cycle (manual, the way Jira works)

Sprints are created, started and completed by the user. Nothing starts or closes on a timer: a
week that went sideways should not be closed out by a clock.

| Action | What happens |
|---|---|
| **Create sprint** | Gets the next key (`SPRINT-n`), an optional name, and dates. The dates default to the next free Sunday 20:00 → Sunday 18:00 week; the first sprint starts now and runs to the coming Sunday. |
| **Start sprint** | Freezes committed points (the sum of what it holds). Only one sprint runs at a time. |
| **Complete sprint** | Freezes completed, added and carried-over points. Unfinished work moves to the next planned sprint — or the backlog when there is none — and its carried-over counter goes up. |
| **Delete sprint** | Only before it starts; its tasks go back to the backlog. |
| **Sunday 19:00** | A push nudge: how the running sprint stands, or that nothing is running. It never changes anything by itself. A nudge more than 3 hours late is skipped. |

- Carried-over tasks count toward the new sprint's committed points once it starts. The old
  sprint does not get credit for them: velocity counts only work that is Done. (Scrum also
  suggests re-estimating remaining work; the assistant offers this during planning.)

## 5a. Item pages

Every goal, task and sprint has its own page, addressable by key:

- `/board/tasks/TASK-7` — title, description, status, sprint, points, priority, goal, comments
  and attachments.
- `/board/goals/GOAL-3` — the same, plus its tasks and derived progress.
- `/board/sprints/SPRINT-2` — dates and name, the frozen or live totals, a points-remaining
  chart drawn from when each task was finished, and the work split by column.

**Comments** are plain text, kept in order, and marked as written by the user or the assistant
(the assistant writes them with `add_comment`). **Attachments** — PDFs, images, documents — are
stored beside documents under a server-generated name, at most 25 MB each.

## 6. Scope changes during a sprint

- Adding a task to a **running** sprint (creating it there, or dragging it in from the backlog)
  asks for confirmation: "SPRINT-2 is running, so adding this is a scope change." If confirmed,
  the task is marked *added mid-sprint* and its points are reported as **added**, separate from
  committed.
- Moving an unfinished task **out** of a running sprint — to the backlog or to another sprint —
  asks the same way and is reported as **removed**.
- Re-estimating a task during the sprint is allowed and does not change the frozen committed
  number.
- The API enforces this, not only the UI: a scope change without acknowledgement is refused,
  so the assistant's tools cannot skip it either.

## 7. Sprint report

A simple list, newest first, one row per sprint:

`Sprint 12 · 13–20 Sep · committed 21 · added 3 · removed 2 · completed 18 · carried over 5`

Plus the average completed points of the last three closed sprints ("velocity"), which the
assistant uses as the default capacity when planning. A sprint's own page adds a
points-remaining chart; there is nothing more elaborate than that.

## 8. Daily planner integration

- The planner stays the **daily** list; the board is the **weekly** list. Reminders stay
  separate and unchanged.
- When adding to a day, the user can **pick a task from the current sprint** (To do or In
  progress) or type a free-form item as today. A planner item linked to a task shows its key,
  and its goal chip when the task has a goal.
- Marking a planner item done does not move the task to Done: a day's work often does not
  finish a task. The user moves the card when it is finished.
- **Morning nudge:** the existing morning brief (Settings → Proactive, default 07:30) says it is
  time to plan the day and lists what is in progress on the board.

## 9. Assistant

Tools (all changes go through the existing confirmation card):

- `get_board` — the running sprint with its three columns, keys, points, goals, carried-over and
  added flags; plus velocity.
- `get_plan` — the running sprint, the sprints planned after it, and the backlog.
- `get_sprint_report` — recent sprints' committed / added / removed / completed.
- `create_sprint`, `start_sprint`, `complete_sprint` — the same manual cycle the user drives.
- `create_task` — title, optional goal key, points, priority, and an optional sprint key.
- `update_task` — title, description, points, priority, goal.
- `move_task` — to a column and/or another sprint by key. A scope change is described on the
  proposal card, and confirming the card is the acknowledgement.
- `delete_task`, `delete_goal`.
- `add_comment` — a note on a task or goal, recorded as written by the assistant.
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
