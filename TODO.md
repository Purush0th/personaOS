# PersonaOS — Task Board

> Shared across all Claude Code sessions. **Claim a task before starting it** (put your
> session name/date in the `claimed` column), tick it when done, and add newly discovered
> work to the right phase. Keep this file truthful — it is the single source of progress.
> Full product spec: [docs/PRD.md](docs/PRD.md).

## How to work in parallel sessions

- **Partition by area** — the safest split is one session per column: `backend` (.NET),
  `mobile` (Flutter), `web` (Angular), `infra` (Docker/CI). Cross-area tasks go solo.
- **Claim before you code**: edit this file first (`[ ]` → `[~] (claimed: <session>, <date>)`).
- Prefer `git worktree` / separate branches when two sessions touch the same project.
- After finishing: mark `[x]`, note anything a follow-up session must know.

Legend: `[ ]` open · `[~]` in progress (claimed) · `[x]` done · `[-]` dropped

---

## Open items (summary, 2026-10-11)

Everything still open, in one place; details are in the sections below. Keep this list current:
add a line when something is left open, remove it when it is done.

Waiting for the owner to say so:
- [ ] Owner to decide: notes from chat (N2) rounds 1–3 not committed; round 3 not deployed.
      Rounds 1–2 are live; round 3 has the server save a plainly asked note itself and takes out
      false claims about other modules.
- [ ] Owner to decide: commit `.continue/rules/personaos.md` and `.continueignore` (local coding
      agent setup), or add them to `.gitignore`.
- [ ] Owner to decide: F1 (reminder claims, web toasts, reply audio on the phone, chat reopening
      the latest conversation) and F2 (intent classification) not committed. Deployed 2026-10-15
      (backup `data/backups/pre-f1f2-2026-10-15`, no migrations) with
      `System.Security.Cryptography.Pkcs`/`.Xml` pinned to 10.0.13: new advisories had failed the
      image's restore. APK `PersonaOS-2026-10-15-arm64.apk` sent.

Not yet tried on the owner's phone:
- [ ] Phone check: Notes screen (built and tested, not yet tried on the owner's phone).
- [ ] Phone check: calendar views C1–C4 (built; only widget tests so far).
- [ ] Phone check: reminder alarm fix (2026-09-23 APK).
- [ ] Phone check: F1 — a reply keeps playing in the server's voice after leaving the chat, and
      the chat opens on the latest conversation (needs the next APK).

AI accuracy (qwen2.5:3b):
- [ ] AI accuracy: planner "no title" scenario fails about 1 in 3 full suite runs since notes
      (10/10 alone).
- [ ] AI accuracy: download link scenario flakes now and then.
- [ ] AI accuracy: the intent check never answers "ambiguous"; "I need to remember this" is read as
      a reminder, and once in three runs a reminder card with a made-up time followed.
- [ ] AI accuracy: the model sometimes asks for a task key after being told which tool to call.
- [ ] AI accuracy: sprint-level column claims ("the tasks are in the backlog, not in the sprint")
      are not checked.

Release and infrastructure:
- [ ] Public release: deliberately blocked by the owner until features are mature and
      hallucination is under control. Do not suggest cutting it.
- [ ] GitHub workflows (`ci.yml`, `release.yml`) never confirmed passing; the GHCR images the
      README's compose file pulls do not exist yet. The release failure (uppercase image
      namespace) is fixed in U1, not yet proven by a release.
- [ ] Upgrade test from one release to the next (keyring survival) and the `minSupportedClient`
      gate: need two published releases.
- [ ] Rename: "PersonaOS" is taken; candidate names checked 2026-10-03, nothing decided.
- [ ] Live Anthropic smoke test (chat with tool calls): blocked on a real API key.

Deferred by the owner:
- [ ] Deferred by the owner: notes widget on the phone ("later").

---

## Planner as a calendar (owner, 2026-10-03)

Owner: "The planner should look like Google Calendar" (Day, Week, Month, Year views and its view
dropdown: Day, Week, Month, Year, Schedule, 4 days; Show weekends, Show declined events, Show
completed tasks). Items can repeat. Reminders stay separate and alarm-grade. Planner items are events
and notify the phone before they start, as an ordinary notification (not an alarm), configured when
the event is created: 5, 10, 15, 25, 30 minutes, 1, 2, 4, 8 hours, 1 day, or custom (a number box
that steps by 1 plus a unit: minutes, hours, days, weeks; when the choice is too large it says what
is exceeded). Answers: repeat edits offer This event / This and following / All events; **one**
alert per event; default for a new event 30 minutes before (the owner's answer to this was garbled,
so Google's default, one constant, easy to change). One session, backend then web then phone.

Design (decided here, so later sessions need not rediscover it):
- **Event** = `PlannerItem`. New: `EndTime`, `NotifyBeforeMinutes` (null = none), `Recurrence`
  (an RFC 5545 RRULE subset: FREQ DAILY/WEEKLY/MONTHLY/YEARLY, INTERVAL, BYDAY, BYMONTHDAY, COUNT,
  UNTIL). No start time = all-day. Events stay inside one day; multi-day events came later (C5).
  Times are the install's local time, as before.
- **A repeating item is one row** (the series); occurrences are expanded on read, never stored.
  `PlannerOccurrence` holds a change to one occurrence (moved, retitled, cancelled, done/skipped).
  "This and following" splits the series: the old one ends the day before, a new one starts there.
- **Alerts**: a dispatcher (30 s tick, like reminders) sends one push notification per occurrence at
  start minus the lead time, once (`PlannerAlertLog`), only while the event has not started. An
  all-day event counts from 09:00 local that day (a constant; flagged to the owner).
- The API keeps `GET /api/planner?from&to` returning days of items (now expanded), so an older phone
  app still works; edits and deletes take `scope=this|following|all` and `occurrence=<date>`.

- [x] **C1. Backend events** (2026-10-06). `PlannerItem` + `EndTime`, `NotifyBeforeMinutes`,
      `Recurrence`; new `PlannerOccurrence`, `PlannerAlertLog`; migration `PlannerCalendar` (additive
      only). `Domain/Services/RecurrenceRule` (parse, expand a window without walking from the start,
      split, move, describe in words; 57 tests). `PlannerCalendar.Expand` applies single-occurrence
      changes; `PlannerQueries.EventsAsync` is the one read used by the service, the prompt and the
      briefs. Service scopes this / following / all (a split keeps a done mark on the day it starts
      from, found against SQLite). `PlannerAlertDispatcher` + `PlannerAlertService` (30 s): one
      notification per occurrence and start, `type=planner_alert` with `itemId`, `occurrenceDate`,
      `date`; body "Starts in 30 minutes · 10:00 am" / "All day today". API: `scope` and `occurrence`
      query on PUT/DELETE/date/status; range limit 400 days (year view). Chat tools: `endTime`,
      `repeat` (daily/weekly/weekdays/monthly/yearly), `repeatUntil`, `notifyBeforeMinutes` (-1 off,
      default 30 on a timed event, filled before the card), `occurrenceDate` for status and move;
      the card shows "until 17:00 · repeats weekly · notify 30 minutes before". 833 tests; a smoke
      script against a local API on SQLite passed 13 of 13.
- [x] **C2. Web calendar** (2026-10-06). `planner/` is now a calendar: `calendar.ts` (pure:
      ranges, titles, overlap layout, repeat presets and custom rules, notification presets,
      `notifyAt`), `time-grid` (Day, 4 days, Week: all-day strip, hours, events side by side, red
      now-line, GMT label, opens at 7 am), `month-view` (dots and bars, "N more"), `year-view` (12
      months, a dot on busy days), `schedule-view`, `event-dialog` (title, day, all day or start and
      end, repeat presets + custom editor, notification presets + custom number with − / + and a
      unit, "You will be notified Fri 2 Oct, 10:00 am" or "Too far ahead… The most is 4 weeks"
      and Save disabled, notes, board task, goal; asks the scope for a repeat), `event-peek`
      (quick view: edit, delete, done, skip), `scope-dialog`. Toolbar: Create, Today, ‹ ›, title,
      view menu with D W M Y A X and Show weekends / skipped / completed (kept in localStorage);
      keys D W M Y A X, T, J/N, K/P, C; view and day in the address (followed, so back/forward
      work). The page takes the full width (`.content:has(app-planner)`). Found by looking: a
      name clash (`end` used by the End time and the custom repeat's "Ends") blanked the End time
      when Custom was picked; a title squashed to 6px in a narrow column; letter-by-letter wrapping
      on a phone. Tests: `calendar.spec` (15), `planner.spec` (8); web 138 pass. One unexplained
      failure in one of seven full runs (not reproduced; name not captured).
- [x] **C3. Phone calendar** (2026-10-07). Done:
      `lib/planner_calendar.dart` (Dart port of the web `calendar.ts`; 15 tests in
      `test/planner_calendar_test.dart`), `PlannerItem` + event fields, `EditScope`,
      `getPlannerRange`, `createEvent`, `updateEvent`, scoped `deletePlannerItem`,
      `setPlannerStatus(occurrence:)`. `screens/planner_screen.dart` is the calendar (title opens a
      date picker, Today, view menu with the views and the three toggles, swipe to step, +);
      `planner_views.dart` (time grid with all-day strip, overlap layout, now-line; month; year;
      schedule), `planner_event.dart` (full-screen editor: all day, start/end, repeat presets +
      custom dialog, notification presets + custom stepper with "Too far ahead … would be …",
      notes, board task, goal; quick view sheet: edit, delete with scope, done, skip, task link;
      scope dialog). View and toggles kept in SharedPreferences. Tapping a `planner_alert`
      notification (background, closed, or foreground via a `planner:` payload) opens the planner
      on that day. Found by looking at rendered PNGs (real fonts): custom notification and custom
      repeat rows overflowed at 360 px, the repeat sheet hid "Custom…", titles broke mid-word in
      narrow week columns. Tests: `planner_calendar_test` (15), `planner_screen_test` (10),
      phone 229 pass. Not yet on a real phone.
      Note: an installed old app gets an error marking a repeating event done (it sends no
      occurrence); one-off events work as before.
- [x] **C4. Accuracy and docs** (2026-10-07). Suite on qwen2.5:3b, 5 runs: 61/65 twice (the
      2026-10-01 single run was 64/65; the other misses are the known flakes: board read flagged,
      asks for a key, download link, 17:00 for "7pm" once). Real regression found and fixed: the
      new optional fields made the model send an end with no start or `repeat:"none"`, which were
      refused, and it asked the user instead of proposing (planner scenario 8/10). Now
      `add_planner_item` drops empty/unusable optional fields before the card and `repeat` takes
      "none": planner scenario 28/30 over three runs (remaining misses: no title at all, the
      refusal that predates this work). PRD §3.4 and the module table, README updated.
- [x] **C5. Calendar follow-ups** (2026-10-08). Owner picked all three: event notifications get
      their own Android channel; drag to move and resize (web, and the phone by long-press);
      multi-day events, drawn as a bar across the top of Day and Week (timed ones over several
      days too). Done:
      **Multi-day.** `PlannerItem.EndDate` and `PlannerOccurrence.EndDate` (migration
      `PlannerMultiDay`, two nullable columns). Null means the event ends on its day; a series'
      occurrences all last as long as its first; one occurrence can be made longer or shorter on
      its own (its `EndDate`, kept when it is moved). A new first day keeps the length; `endDate`
      on an update sets the last day, counted from the day the person was looking at. A timed
      event over several days needs an end time, which may be earlier in the day than the start.
      `PlannerQueries.EventsAsync` finds events that began before the range; `GET /api/planner`
      lists each once, on its first day in the range (so a client never sees it twice; an old app
      shows it on that day only); a single day's read includes every event covering it. Prompt:
      "2026-10-10 to 2026-10-14 Goa trip"; morning brief "(until Wed 14 Oct)"; the evening rollup
      counts an event on its last day only; notification "From today to Sun 11 Oct" or "… · until
      Sat 10 Oct". Chat tool `add_planner_item` takes `endDate` (dropped when not after `date`);
      card "until 2026-10-11". Also fixed: an "All events" move from an occurrence that had been
      moved on its own shifted the series by the wrong number of days (now counted from where it
      is shown). Web and phone: bars in lanes across their days (cut square past the edge of the
      view), Month lanes with "N more" / "+N", Schedule "(Day 2/3)" with "From 6pm" / "Until
      9am", quick view "Friday, 9 October – Sunday, 11 October", editor with a start day and an
      end day.
      **Drag.** Web (`drag.ts`, `drag.scss`): mouse or pen (a finger scrolls), 4 px threshold,
      15-minute snap; a timed event to another time or day, its bottom 8 px to change the end,
      a top bar or a Month event to other days; a ghost shows where it lands; the change shows at
      once and a repeating event asks the scope (Cancel puts it back). Found by trying it: the
      click the browser sends after a drag opened "New event" where the drag ended (now
      swallowed); the scope dialog drew a scrollbar for 4 px; the header and the strip were
      misaligned with the hours by the scrollbar width; Month times spilled into the next
      column at 390 px. Phone: long press (with a haptic) in Day, 4 days and Week, the same
      moves; no autoscroll while dragging and no drag in Month (Google's phone app has none
      there either).
      **Channel.** `IPushSender.SendAsync(channel)` with `PushChannels.Reminders` /
      `PlannerEvents`; the phone creates "Event notifications" (`planner_events`, high). An app
      older than this shows them in a default channel.
      Tests: backend 857 (`PlannerMultiDayTests`, tool and prompt cases), web 144, phone 241
      (`planner_calendar_test`, `planner_screen_test`: long-press move, resize, scope, bar).
      SQLite smoke 16/16 (`EndDate` queries) and the earlier calendar smoke still pass.
      Accuracy suite (qwen2.5:3b, 5 runs): 63/65, planner scenario 5/5; the two misses are in
      unrelated scenarios (board read via get_sprint_report once, a delete_goal refusal once).
- [x] **C6. A notification when an event starts** (2026-10-10). Owner,
      after trying C5 on the phone: "when its start no notification receives - usually google will
      notify like the event is started". First cut: a timed event with a notification also gets
      "Starts now" at its start, replacing the heads-up on the phone. Done: `PlannerAlertDispatcher` sends the
      heads-up before the start and "Starts now · 10:00 am" from the start to 5 minutes after;
      `PlannerAlertLog.AtStart` (migration `PlannerStartAlert`) keeps each to once. Both carry
      the FCM tag `planner-<id>-<occurrence>` (`IPushSender.SendAsync(tag)`), so the second
      replaces the first; the phone uses the same tag for one it shows in the foreground. Editor
      hints say "…, and when it starts". Then the owner widened it (same day): every event is
      notified at its start, even with "No notification" (that choice means no heads-up; the start
      notification is not an option in the list), and an all-day event starts at 12 AM:
      `PlannerEvents.AllDayAlertReference` is now midnight (was 9:00), so its heads-up counts back
      from 12 AM ("1 hour before" = 11 PM the night before), web and phone hints alike; the
      editor says "You will be notified when it starts." with no heads-up. Tests: backend 859
      (start for every event, all-day at midnight and 11 PM, the 5-minute limit, done).
      Owner then chose to keep both on the phone rather than "Starts now" replacing the heads-up:
      the tags are now `planner-<id>-<day>-before` / `-start` (also sent as data `tag`, which the
      app uses for one it shows while open), so only a resent heads-up replaces an old one.

## Notes (owner, 2026-10-10)

Owner: "Notes where to capture simple todo like items where i can jot down things popup in my
mind. later i can work on it ... Something like Google keep." Then: "no complexity. No offline
support and keep it simple stupid notes". Answers: a note from chat saves without a card;
module named Notes; no home-screen widget for now; no tags ("we should move the items to
backlogs asap / notes are not permanent place").

Design: a note is plain text (one line or a few), newest edited first. Add, edit, delete, and
**Move to backlog** (a board task: first line the title, the rest its description; the note is
removed). Module `notes`, on by default (existing installs switched on by the migration). Chat
tools `create_note` (saved straight away, receipted), `get_notes`, `move_note_to_backlog` (a
card, as every other board change). No offline, pins, done state, tags, share target or
shortcuts.

- [x] **N1. Notes** (2026-10-11). Server: `Note` (text, 2,000 characters at most), migration
      `Notes` (switches the module on for existing installs), `NoteService` (list with search,
      create, update, delete, move to backlog: first line the title, cut at a word past 200
      characters with the whole note kept as the description; refused while the board is off),
      `NotesController` (`/api/notes`, `POST /api/notes/{id}/to-backlog`), chat tools
      `create_note` (no card), `get_notes`, `move_note_to_backlog` (card, offered only with the
      board on). Web: `/notes` in the nav, "Take a note…" (Enter saves, Shift+Enter new line),
      cards in a grid read left to right, click to edit in place (Ctrl+Enter or leaving saves,
      Escape cancels, emptied = deleted), Move to backlog and Delete on hover (Undo), search.
      Phone: Notes card on home, the box on top (keyboard send key saves and keeps the
      keyboard), cards two to a row, tap for a full-screen editor (back saves; bar: move to
      backlog, delete), long press for the same two, Undo, search. Found by trying it in the
      browser: typing the next note while one saved was wiped when the save answered, and
      with fast typing the box did not clear (a bound value changed and changed back before a
      redraw); the box is now cleared and read directly. Tests: backend 871 (`NoteTests`),
      web 149 (`notes.spec`), phone 253 (`notes_screen_test`, layout sweep). Accuracy suite: new
      scenario "saves a note" 5/5 and 3/3; full suite 40/42 (planner once, then 10/10 on its own;
      download link once, a known flake).
- [x] **N2. Notes from chat, as the owner found them** (2026-10-11). "Can you add a note to meal
      prep today?" saved "Note to self: Prepare dinner ingredients for tomorrow", and the reply
      ("I've noted that…") was flagged "nothing was saved" with an odd sentence from the
      memory correction round. Causes and fixes: (1) a note saves without a card, so a reworded
      one went unchecked: `NoteWordsGuard` sends a `create_note` back when fewer than half its
      telling words are the user's (prefix match, "prep"/"prepare"), with the user's message;
      the tool's description gives the example. (2) "I've noted that" read as a promise to
      remember, and a saved note did not count: `ClaimCheckGuard` now treats any change saved
      straight away this turn (`ChatTurnState.WriteTools`: notes, auto-saved memories) as
      backing the reply. (3) 2 in 10 notes kept "add a note to" / "note to" in front:
      `CreateNoteTool.Clean` drops it ("Notes from the meeting" keeps its first word). Accuracy
      suite: new scenario "note in the user's words" (the owner's sentence; checks the text,
      the day and no false flag) 10/10, "saves a note" 10/10. Full suite 43/45 (download link
      once; "knows tomorrow" once answered Tuesday 13th on Sunday 11th, a new miss with no tool
      involved, worth watching). Backend 889. Deployed (not committed).
      Then (same day) "Add a note to cook today": no note saved, "I've noted that…", and the
      memory check's correction asked for create_memory, which the model answered with that
      check's own words ("Nothing worth remembering was said, so nothing was remembered"). Now a
      "noted" claim is about a note when notes are on and the user asked for one (or memory is
      off): `NoteClaimInstruction` asks for create_note with the user's message; the memory
      check's wording no longer has the phrase the model echoed; the claim is also caught with
      memory off. `NoteWordsGuard` also drops a leading "Note" the request brought along ("Note
      meal prep today", 1 in 15) unless the user wrote "note <that word>". Accuracy: new
      scenario "note to cook today"; the three note scenarios 45/45 over 15 runs; full suite
      46/48 (planner "missing information" once, the known no-title flake; goals listing once).
      Backend 894. Deployed; the owner then found it still failing in chat jdeu9b8n: there the
      model had said "I've noted that…" four times before and copied itself, correction round
      included (a fresh chat saved every time, which is why the suite passed). Now
      (`NoteRequest`): when the message plainly asks for a note ("add a note to …", "note: …",
      "note down that …", "jot down …"; not "add a note to my planner" or to TASK-5) and no note
      was saved, the server saves it from the user's words and the reply becomes "Noted: “…”."
      Also, a saved note backs claims about itself but not about another part of the app: "This
      note has been added to the backlog" (seen once) is taken out of the reply. Checked by
      sending the owner's messages in one chat, new and continued: 6 of 6 saved, no flag. Full
      suite 46/48 (planner no-title flake again, 1 in 3 in the full runs since notes but 10/10
      alone, worth watching; download link). Backend 909.

## Owner feedback F1 (owner, 2026-10-11 evening)

Owner, after trying the phone: start notifications work, faster-whisper and Kokoro are good. Then:
"the model having hallucinations with reminder (chat dawdsabu) — it is claiming to added a
reminder but not"; "web can show toasts for notifications"; "when i switched page while the chat
is playing the audio response the audio stopped and the voice switched to tts"; "Whenever i opened
the chat it always showing new one but the last active one can be preserved if not new chat is fine".

- [x] **F1a. Reminder claims with no card** (backend). In dawdsabu the owner discarded a 6 PM card
      and answered "tomorrow 9 a.m."; qwen2.5:3b then said "I have scheduled a reminder" three
      turns running, the correction round included, and never proposed one. Reproduced locally only
      with the native Ollama provider the live server uses (`ollama`, which sends `num_ctx` 16k);
      the OpenAI-compatible endpoint sends no context size, Ollama cut the history short and the
      model proposed every time (but with made-up titles). Replay script: 1 of 4 runs right, 5 of
      12 follow-ups with a card. Changes: (1) a claim a reply was flagged for is left out of the
      history the model reads (`History/TurnRecord`; a reply that was only the claim is not sent);
      (2) the correction names the tool when the claim names what it changed (`ChangeToolFor`:
      reminder → create_reminder, event/calendar → add_planner_item, goal, task/backlog) and quotes
      the user, and "I have noted that you need to remind yourself…" goes there rather than to the
      memory correction; (3) "I have proposed a reminder… waiting for your confirmation" or "confirm
      it below" with no card is a claim too (`ActionClaimDetector.FindProposalClaim`); (4) a claim
      that survives the correction gets one focused try offered only that tool and the last six
      turns; a card from it replaces the reply with the usual "Here is the change I would make".
      Tried and dropped: writing "[Done in this reply: …]" records into the history made it worse
      (0 of 6): the model wrote such records itself instead of calling the tool. Result: 21 of 24
      follow-ups with a correct "Buy milk — 09:00" card; the misses keep the "nothing was saved"
      warning. Accuracy suite on the native provider 30/32 (download link 0/2, the known flake).
      Backend 926. The local dev instance now uses the `ollama` provider like the live one.
- [x] **F1b. Web toasts for notifications** (web). `core/alert-toasts.ts`: while signed in and the
      app is open, a toast (with Open) for a pending reminder at its due time, and for each planned
      event when it starts (all-day at 12 AM) plus the heads-up before it when one is set, as the
      phone gets them. Reads reminders and today–tomorrow's planner every 5 minutes and when the tab
      comes back; alerts already past when the app opens are not shown; a tab that slept shows what
      came due in the last 5 minutes. Timers run outside Angular's zone. Reminder due times come
      without a zone suffix and are read with `parseUtc` (read bare they were 5.5 hours in the past
      in India and never shown). Verified headless: "Reminder: Buy milk" and "Starting now: …" at
      the exact second.
- [x] **F1c. Reply audio after leaving the chat** (phone). Leaving the chat disposed its voice
      service, the server's audio stopped, and the cut-off playback was taken for "server voice
      unavailable", so the phone's own TTS read the reply. Now one voice for the app session: the
      reply finishes playing, the reopened chat can still stop it, and a reply stopped on purpose
      never falls back to the phone's voice (`VoiceService._speechTurn`). Leaving still closes the
      microphone (`release`). Not yet tried on the phone.
- [x] **F2. Intent classification before every message** (backend, 2026-10-12). From
      `docs/model-accuracy.md`. Owner: the doc's phrases are examples; "the idea is we need to
      classify the intent"; classify before every message (option a). Audit first: routing (tool
      registry, `OfferedToolGuard`), cards (`ConfirmationGate`), receipts, claim checks and duplicate
      guards already met FR-02/04/05 and AC-05; missing were a classification stage, structured
      output and asking when unclear. Built: `Ai/Intents/IntentClassifier` (one short call per
      message where write tools are offered: last 4 messages, temperature 0, answer held to a JSON
      schema of the intents on offer; one example per intent), `ChatIntents.CreatingTool` (the
      router: intent to tool, explicit table), an optional `AiRequest.ResponseSchemaJson` (Ollama
      `format`, OpenAI `response_format`, Anthropic ignores it). The chat model gets a one-line
      intent hint; when the turn made nothing the intent asked for, the focused try (F1a) runs
      with that tool, told to ask rather than guess; its question becomes the reply. No focused
      try after a refusal, after a lookup that answered without claiming anything, or when the
      reply asks the user something. Measured (qwen2.5:3b, native Ollama): new intent scenarios
      18/24 before, 23/24 → 30/32 → full suite 70/72 (unclear asks once made a reminder card;
      download link once); dawdsabu replay 6/6 runs, 18/18 follow-ups with a card. About 0.9 s
      more per message. Known limits: the classifier never answers "ambiguous" for "I need to
      remember this" (says create_reminder); the focused try then usually asks, once in three it
      proposed a reminder with a made-up time. Backend 940. Not committed or deployed.
- [x] **U1. Updates built into PersonaOS** (2026-10-24). Owner: prod server is `192.168.2.10`
      (RTX 3050 6 GB, 8 GB RAM), no SSH for Claude, the owner installs the prerequisites;
      "automated deployment when we push release" as part of the application, "think of how our
      user will receive the update"; voice on the server. Found: every release since alpha.4
      failed in `images` ("repository name must be lowercase": owner Purush0th), so no image was
      ever published; the API reported a constant 0.1.0; checks used GitHub's "latest", which
      skips prereleases. Built: release workflow (lowercase namespace, `VERSION` build arg into
      the API, whisper and updater images, APK `--build-name`/`APP_VERSION`, `import-data.sh` as
      an asset); `ReleaseVersion` (SemVer order); `IReleaseFeed`/`GitHubReleaseFeed` (list, 10-min
      cache), `IUpdater`/`FileUpdater` (request.json/status.json in the shared `updates` volume),
      `UpdateService` (prerelease servers follow prereleases; only a newer published release;
      auto-install skips a rolled-back version), `UpdatesController`, `UpdateCheckService` (15 min),
      `InstanceConfig.AutoInstallUpdates` (migration `AutoInstallUpdates`); `deploy/updater`
      (Python on docker-cli: backup tarball to ./backups, set PERSONAOS_VERSION, pull, up
      --no-deps all but itself, wait for /api/branding to report the version, roll back; a helper
      container replaces the updater afterwards; "not-set-up" when PERSONAOS_DIR is wrong);
      compose profiles `updater` and `ollama` (6 GB settings: flash attention, q8_0 cache, one
      model, one request); web Settings → Updates + banner Update now (polls while updating,
      reloads on the new version); phone notice for the server, then for a newer app (APK link).
      `.gitattributes` keeps .sh/.py LF. Tests: backend 960, updater 8 (CI job added), web 164,
      phone 259. Not committed. GHCR packages may start private: make them public after the first
      release, or the server needs `docker login ghcr.io`.
- [x] **P1. Send test notification** (2026-10-24). Owner, after a first push worked on the new
      server: "we need to add test functionality for the firebase". Settings → Push notifications
      → Send test notification (`POST /api/setup/push/test`, `PushTestService`): one notification
      to every registered phone, Firebase's answer per phone shown, a token Firebase rejects for
      good removed; says so when push is off or no phone is registered. Phones register without a
      name, so they show as "android". Backend 964, web 166. Not committed.
- [x] **P2. The phone app updates itself** (2026-10-24). Owner: "apk should detect update from
      github and i can update from the app itself... without manual download and installation".
      Found: release APKs were signed with the runner's debug key, new on every run, so no release
      could ever update the last (why alpha.9 needed an uninstall). Now: release signing from
      `android/key.properties` (written by the release workflow from the ANDROID_KEYSTORE_*/KEY_*
      secrets; the workflow fails without them); `AppUpdater` downloads the release's APK (only a
      github.com release of this repository) into cache/updates and hands it through the
      `personaos/installer` channel (MainActivity, FileProvider `${applicationId}.updates`,
      REQUEST_INSTALL_PACKAGES) to Android's installer; `UpdateNotice` on the home screen with
      Update and progress, and the one-time "allow installing apps" step. Android's own confirm
      screen cannot be skipped. Phone tests 265. Needs the owner to create the key and set the
      secrets (README, "Signing the phone app"); the first signed APK needs one uninstall.
- [x] **P3. Whisper wrote Chinese** (2026-10-24): the phone sent no language, so whisper guessed
      per recording and picked Chinese on short clips. The phone now sends its own language
      (`PlatformDispatcher.instance.locale.languageCode`) with each recording.
- [x] **P4. Phone update fixes + About screen** (2026-10-29). The phone never offered alpha.11:
      dismissing the server's notice of a release also hid the app's update to it (dismissals are
      now kept apart), and the notice was worked out only at launch (now again on resume, at most
      every 10 min). New About screen (info icon on home, no sign-in): app and server versions,
      server address, "Check for updates" (ignores dismissals, shows errors), the Update button
      (`UpdateInstallButton`, shared with the home notice), licences. `test/about_screen_test.dart`.
- [x] **R1. Server and phone app released apart** (2026-10-29). Owner: separate web and mobile
      builds, keep the min version check, Android only for now. `v*` tags build the server + web
      (`release.yml`, no APK any more); `mobile-v*` tags build the signed APK
      (`release-mobile.yml`, `make_latest: false`). The server ignores app releases (`IsApp`;
      Install refuses them) and reports the newest one as `LatestApp` (web Settings → Updates
      links it). The phone updates only from `mobile-v*` releases and, below the server's
      `MinSupportedClient` (now `0.1.0-alpha.13`), shows a non-dismissible "Update this app"
      card; About says it too. Apps up to alpha.13 cannot see `mobile-v*` releases: install the
      first one by hand.
- [ ] **iOS: through the web app first** (owner, 2026-10-29: "for iOS, we need to make the web
      interface better"). No native iOS build for now; make the web app work well on an iPhone.
- [x] **W1. Web Interface Guidelines pass** (2026-10-29, Vercel guidelines, whole web app). Keyboard
      way into the planner grid (all-day cells are buttons; a "New event" button per day column
      shows on focus, global `.slot-add` because time-grid.scss is at its 4 kB budget); focus ring
      on the notes search and note box; notes box focuses only with a fine pointer (no keyboard
      popping over notes on phones); chat messages are `role="log"` with `aria-busy` while
      streaming; `theme-color` meta, kept in step with the light/dark pick by ThemeService;
      tabular figures in reports and the burndown; board drag honours reduced motion; planner and
      reminders write times one way (`formatTime`, no hardcoded `en-GB`); URL fields are
      `type="url"`, no spellcheck/autocomplete; Settings asks before leaving unsaved edits
      (`unsavedChangesGuard` + `beforeunload`); dark tertiary container overridden in styles.scss
      (the blue palette gave pure #0000ef: update banner, board WIP note, chat notes). Kept: sentence case (Material 3), no list
      virtualization (single-user volumes), dialog autofocus.
- [x] **W2. "Sky" restyle of the web app** (2026-10-29, owner picked the Techo AI Dribbble shot after
      trying editorial, Swiss, violet and black-and-gold). Tokens in styles.scss: cool greys, one
      blue `#2563eb`, Plus Jakarta Sans (matched to the shot by eye), semibold headings (Material
      bakes weight into its type shorthands, so they are rewritten), softer corners. Shell: grey
      rail with a blue New chat button (the history list lost its own), each page on a white
      rounded panel (`--shell-bg`). Chat: sky gradient, dotted mark and two-tone line when empty,
      composer is one card with the mode chips and a round send (styles in `chat/composer.scss`,
      apart for the 4 kB budget). Released as v0.1.0-alpha.14.
- [x] **M2. "Sky" restyle of the phone app** (2026-10-29). Scheme written out to the web tokens in
      `_personaOsScheme` (main.dart), Plus Jakarta Sans bundled in `assets/fonts` (variable weight,
      OFL; `personaOsFont` in layout.dart — a button's own `textStyle` or a TextPainter must name it,
      or it falls back to Roboto), semibold headings, 12–16 dp corners. Chat: `skyWash` gradient,
      white bordered replies, blue own messages, composer card with a round arrow send, the web's
      empty-state line. Home hero on the sky; alarm screen moved from violet to navy and blue.
      Pending-action cards and receipts now take scheme colours (the fixed amber stayed pale in dark;
      the receipt green failed contrast on dark).
- [x] **U2. Live on the prod server** (2026-10-24). Fresh install (no data moved, owner's choice) on
      Windows 11 + Docker Desktop (WSL 2) at `C:\personaos`, Ollama native on Windows
      (`host.docker.internal:11434`), profiles speech + updater with
      `PERSONAOS_DIR=/run/desktop/mnt/host/c/personaos`. alpha.9 installed by hand; alpha.10
      installed by the updater from Settings → Updates (backup in `backups\`, rolled forward,
      updater replaced itself) — the updater works through Docker Desktop. Push verified with a
      planner event. The phone needed one uninstall for the new signing key. alpha.10's release
      attached ".env.example" as "Default.env.example" (GitHub renames dot-files): now `env.example`.
- [x] **F1d. Chat opens the latest conversation** (web and phone). The phone opens the most recently
      used conversation (on any device) when the chat opens; "New chat" and History work as before.
      Web: plain `/chat` goes to the latest; "New chat" links to `/chat?new=1` (also used after
      deleting the open one or a dead link, so it cannot loop). Tests: phone 256, web 160.

## Maturity: phone UX, accuracy, engineering standards (owner, 2026-10-01)

Owner: "focus on feature and UI polishing and ux for phone, esp the full screen and quick looks
the same size and features in mobile, not sure will help in tablet. I need to mature this product
in terms of accuracy: research and make appropriate changes. Finally a well-architected, principal
engineer level coding standard." Worked in this order; one session (backend+web+mobile).

**Phone UX**
- [x] **U1. Quick view and full page differ on purpose** (2026-10-01). The task quick view was
      the whole edit form in a near-full-height sheet and the full page was the same form. Now:
      `TaskQuickView` is a short look (key, status chip, title, priority, points, sprint, goal
      chip that opens the goal, four lines of description, comment/file counts, carry-overs) with
      the common moves and "Edit, files and comments"; `TaskPage` is the editor, which stays open
      after Save ("Saved.") and reloads, closes on Delete, and from 840 dp puts a Details panel
      (status and moves, fields, Save/Delete, dates) beside the text and discussion. The new-task
      sheet only asks what a task needs to exist. `GoalQuickView`: key, type, slot, title, dates,
      priority, parent, progress with what drives it ("0 of 1 task done"), description, Complete/
      Reopen, Open goal. `GoalPage`: everything, plus a menu with every action of the goals list
      (progress, add task, add child goal, move, delete); from 840 dp files and comments sit
      beside the goal. `showQuickView` (layout.dart) shows a sheet below 600 dp and a dialog at
      most 560 wide above it. `board_screen.dart` split: `board_widgets.dart` (scope
      confirmation, sprint labels, card, pills, `StatusChip`, `boardMoveTargets`) and
      `task_views.dart`. Checked by rendering phone (light) and tablet (dark) goldens.
- [x] **U2. Every place that opens a task or goal uses the same entry points** (2026-10-01): board
      card, backlog row, planner links, goal tasks and child goals, reports, sprint page, goals
      list and timeline go through `showTaskQuickView` / `showGoalQuickView`. New: task and goal
      keys in chat replies are links (`item_links.dart`; never inside code, existing links or for
      keys the server could not find) that open the quick view. Tests: `item_links_test`,
      `chat_item_links_test`, quick view/full page tests in `item_views_test` and `board_test`.
- [x] **U3. Visual sweep, phone and tablet, light and dark** (2026-10-01). Rendered 12 screens
      (board sprint and backlog, sprint page, goals, timeline, planner, reminders, conversations,
      memories, settings, login, chat) at phone, tablet portrait and tablet landscape (dark), with
      realistic data. Fixed: the Backlog section squeezed "Backlog" into a column of letters
      beside "Create sprint" on a phone (the control now goes under the title below 520 dp); chat
      bubbles were capped at 320 px on every screen (now 82% of the width, at most 600); the
      login form stretched across a tablet (now at most 480 wide). The rest read well: lists
      stay readable width, the board shows its columns side by side, the timeline fills the width,
      lists end above the add button. (Black outlines round buttons in test renders are Flutter's
      test-mode shadows, not the app.)

**Accuracy** (research: Anthropic "Reduce hallucinations"; Qwen function-calling guide; Ollama
sampling defaults; findings in the entries)
- [x] **A1. Sampling made explicit, and measured** (2026-10-01). `InstanceConfig.AiTemperature`
      (migration `AiTemperature`; set or cleared through `PUT /api/setup`, negative clears; shown
      with the profile default in `GET /api/setup`), `ModelProfile.Temperature`, sent by the
      Ollama (`options.temperature`) and OpenAI-compatible (`temperature`) adapters. Anthropic
      never gets one: models after Claude Opus 4.6 refuse any value but 1.0 (the SDK marks the
      field obsolete). **Finding: a forced low temperature made qwen2.5:3b worse, not better** —
      0.8 (Ollama's default) 35/39 = 90%, 0.2 31/39 = 79%, 0.0 30/39 = 77%: a low temperature
      repeats a mistake on every try. After the fixes below 0.8 and 0.2 scored alike (64/65 and
      63/65). So no profile sets a temperature; the setting is there for an admin who wants one.
      The OpenAI-compatible adapter now takes its `HttpClient` (tested like the Ollama one) and
      disposes its request message.
- [x] **A2. Accuracy suite** (2026-10-01). `scripts/model-check.ps1`: 13 scenarios (6 new, each
      from an incident: tomorrow's date, a backlog task's points, backlog vs sprint, an unknown
      task, a reminder in the past, the download link), `-Runs` for a pass rate per scenario
      (PASS / FLAKY / FAIL), `-Temperature`, `-Only`, `-Json` with the full reply of every failure.
      Seeds a backlog task and deletes it; restores model and temperature. Run it against a local
      API (`.claude/launch.json` "api") with Ollama; numbers for this round, qwen2.5:3b-instruct: start 35/39 (90%, 3 runs); final 64/65
      (98.5%, 5 runs), the one miss naming "GitHub releases" without the link.
- [x] **A3. Claim checks and the failures the suite found** (2026-10-01).
      - `ItemFactGuard`: "TASK-1 is in the running sprint" said of a backlog task, and keyless
        "the tasks are in the backlog, not pulled into the sprint" / "the sprint is empty" against
        a running sprint with tasks (or "the tasks are in the backlog" with an empty backlog).
      - New read tool **`get_task`**: one task by key wherever it is (the board lists only the
        running sprint, so the model said a backlog task "is not listed"); facts in words; the
        lookup round prefers it when the text names a key.
      - `create_reminder` took internal ids (`goalId`, `plannerItemId`); at low temperature the
        model invented `plannerItemId: 123` every time. Now `goalKey` ("GOAL-3"), and each link
        says to leave it out unless the user asked.
      - `add_planner_item`: the day is optional (today), filled in before the card through a new
        `IPersonaTool.CompleteInputAsync` / registry `CompleteAsync` step, so the card shows the
        day and Confirm runs what was shown; a call with neither title nor task is refused.
      - New tool-call guard **`RelativeDateGuard`**: a change for another day than the "today",
        "tomorrow" or "yesterday" the user said (and no other date in the message) is sent back
        with the right date. Reminder-for-tomorrow went from 4/5 to 5/5, past-reminder 4/5 to 5/5.
      - `LeakedToolCallScrubber` strips Qwen's `<tool_call>` tags (a reply was only those), and an
        empty reply is asked for once more before "I did not manage to write an answer".
      - `ChatService` takes "today" from its `TimeProvider`, so tests pin it.
      Still open: the model sometimes asks for a key after being told which tool to call (not seen
      in this round's runs); one board read was flagged as an unverified claim once in 22 runs and
      could not be reproduced (the suite now keeps the full reply to catch it).
- [x] **A4. Grounding rules in the prompt** (2026-10-01). `tool-rules` (full and compact): answer
      about the user's data only from a tool result or the instructions, values exactly as given;
      "if neither says it, you do not know it — say so or read it"; a task key can be anywhere, so
      read it before saying it does not exist. From Anthropic's "Reduce hallucinations" guide
      (allow "I don't know", restrict to provided information).
- [x] **A5. Owner decisions** (2026-10-02). (1) The owner switched live to the native **Ollama**
      provider (`http://host.docker.internal:11434/v1`; the adapter drops the `/v1`), so it sends
      `num_ctx` 16,384 and `keep_alive` itself; connection test passes. (2) **Model stays
      qwen2.5:3b-instruct**: production will run on an **RTX 3050 6 GB**, so a 7B model is not
      the target. The 3B model at Q4 (about 2 GB) plus a 16k context fits that card. Deployed
      2026-10-02 (backup `data/backups/pre-maturity-2026-10-02`, migration `AiTemperature`
      applied); APK `PersonaOS-2026-10-02-arm64.apk`.

**Engineering standards**
- [x] **S1. `docs/ENGINEERING.md`** (2026-10-01): principles, architecture and layer table, tool
      and guard rules (keys not ids, optional fields say when to leave out, facts in words,
      validate before the card, every guard names its incident, AI changes measured), C#, Dart
      and TypeScript rules, testing table, review checklist, definition of done. CLAUDE.md lists
      it; CONTRIBUTING.md links it and lost its stale "native Anthropic" and "SQL Server" lines.
- [x] **S2. Enforced** (2026-10-01). `src/backend/Directory.Build.props`: nullable, implicit
      usings, **warnings as errors**, `AnalysisLevel latest-recommended`, code style in the build
      (removed from the five csproj files). Root `.editorconfig`: file-scoped namespaces as a
      warning, migrations as generated code, and the four rules turned off with the reason
      (CA1848/CA1873 logging delegates, CA1716 VB keywords, CA1859 concrete types; tests allow
      underscores). Every other finding fixed, including real ones: the Anthropic stream ignored
      cancellation (CA2016), prompt and brief dates were formatted in the server's culture
      (CA1305: a non-Gregorian culture would have put a wrong year in the prompt), a push helper
      took its token mid-list. The API Dockerfile copies `.editorconfig` and the props (image
      checked to build). Phone: `analysis_options.yaml` with strict casts, inference and raw
      types and 15 more lints, `dart format` at 120 columns; all 49 findings fixed, among them
      fire-and-forget futures now marked `unawaited`, and a latent bug: Memories' auto-save read
      used `.catchError((_) {})` on a `Future<bool>`, which throws if the call fails.
- [x] **S3. Architecture tests** (2026-10-01): `PersonaOS.Tests/Architecture/ArchitectureTests`
      (Domain on nothing; Application not on Infrastructure, Api, EF SQLite, Anthropic, Firebase or
      ASP.NET; Infrastructure not on Api; one file uses the Anthropic SDK; no controller touches
      the database). It found one: `ProactiveController` queried `IAppDbContext` itself; now
      `IProactiveService.GetRecentRunsAsync`.
- [x] **S4. Largest files split along their seams** (2026-10-01), no behaviour change: phone
      `board_screen.dart` 1,149 → 589 (+ `board_widgets.dart`, `task_views.dart`),
      `goals_screen.dart` 1,074 → 527 (+ `goal_forms.dart`), `chat_screen.dart` 1,099 → 685
      (+ `chat_messages.dart` as a part); backend `BoardService` 813 → 359 (+ `.Sprints`,
      `.Tasks`, `.Nudge` partials), `ChatService` 702 → ~500 (+ `.Instructions`,
      `.Conversations`). Not done: an ESLint setup for the web app (needs new npm packages;
      `strict` TypeScript and strict templates already gate it).

## Pivot: goal roadmap, memory, modes, speech (owner, 2026-09-26/27)

The owner revised the PRD (docs/PRD.md, revision of 2026-09-27) and answered the open questions.
Order agreed: P0 -> P1 -> P2 -> P3 -> P4 -> P5 -> P6. Requirements live in the PRD; this list is
the work and its status. Decisions not written in the PRD:

- **Live data wipe at P1 go-live:** keep reminders and settings (instance config, admin login,
  AI provider and its key, push config, registered devices); delete everything else — goals,
  tasks, sprints, comments, attachments, planner items, conversations and pending cards,
  documents, brief history. The owner says the current data is not real. Take a full backup of
  the `personaos_api-data` volume first, and confirm with the owner right before running it.

Settled 2026-09-27 (in the PRD too): only **active** child goals block completing a parent; the
**dropped** status is removed entirely (goals are active or completed; a goal that no longer
matters is deleted); a goal **with child goals cannot be deleted** until they are; the wipe
**keeps About you** (the owner accepts losing it only if a migration needs that); the web's
memory list is a **Settings section**.

Dates, decided 2026-09-27 after P0 (supersedes "standalone goals keep today's due-date rules"):
**every goal follows the calendar.** The user picks the type and the slot and the dates follow;
a goal never starts in the past. Year: starts today or a later day picked, ends 31 December, at
least 90 days ("3 months or 1 quarter"). Quarter: Q1–Q4 of the current or a later year, starts
on the later of today and the quarter's first day, ends on its last day, at least 45 days. Month:
a calendar month, same way, at least 15 days. The end date is the due date; the start never
changes on edit. Moving a goal shifts its dates to the new slot; a goal's type changes only when
it is not under a parent.

- [x] **P0. Docs** (2026-09-27). PRD adopted from the owner's revision with its broken table rows
      fixed and the 2026-09-27 answers written in: memory auto-save is the one exception to
      "nothing writes without confirmation" (receipt after saving; edit and delete always), goal
      progress averages child goals with equal weight, nested goals follow calendar quarters and
      months while standalone goals keep today's due-date rules, completion is manual and blocked
      by open tasks or active child goals, delete offers keep / delete / reassign with a per-task
      mapping, moves ask for the calendar slot, Timeline is Gantt-style on web and phone with a
      year dropdown, modes are picked in the chat box with no write tools in Brainstorm and
      Reflect, speech goes phone -> API -> OpenAI-style audio service, no web voice.
      docs/sprint-board.md: goal nesting and task-count progress.
- [x] **P1. Goal hierarchy (backend)** (2026-09-27). `Goal.ParentGoalId` (migration `GoalHierarchy`:
      deletes dropped goals the way a delete would, then adds the column with its foreign key in
      place — an `ALTER TABLE … REFERENCES`, not EF's table rebuild, which runs outside the
      migration's transaction; checked on a copy of the dev DB: integrity and foreign keys clean).
      `Domain/Services/GoalCalendar` replaces `GoalPeriodCalculator`: every goal follows the
      calendar (see the dates decision above), labels "2026" / "Q4 2026" / "Oct 2026". `GoalService`
      (now with config, board and clock): nesting by type pair, slot inside the parent and free
      (so at most 4 quarters / 3 months), progress by `GoalProgressCalculator.ComputeAll`
      (completed 100, else average of child goals, else done tasks ÷ tasks, else manual), manual
      progress refused on a goal with tasks or children, completion refused with open tasks or
      active children (`CompleteProblem` on the DTO says why), reopening refused under a completed
      parent, move with the slot chosen and child months keeping their position, detach keeps
      dates, type/slot change only for a standalone goal without children, delete refused with
      child goals and otherwise keep / delete / per-task reassign. Every change has a Validate twin
      the tools call, so a card that would fail is never shown. `dropped` is gone everywhere
      (status, `includeDropped`). Board: a task links only to an open monthly goal, and a done
      task under a completed goal cannot be reopened. API: `GET /api/goals` flat with `parentId`,
      `slot`, `children`, `completeProblem`; `PUT /api/goals/{id}/parent` moves; `DELETE` takes an
      optional body `{taskAction, reassign}`. Tools: `create_goal` (year / quarter / month /
      parentKey; month names and "Q4" accepted; a slot without a year is the next one),
      `update_goal_status` (active/completed), new `move_goal`, `delete_goal` with taskAction and
      reassign; `create_task`/`update_task` check the monthly rule before the card. Cards name the
      slot and what happens to a deleted goal's tasks; receipts show the slot. Prompts: `goals`
      explains nesting and lists goals parents-first with "under GOAL-n"; `board` says tasks sit
      under monthly goals and points never set progress. `get_goals` is a flat list with `under`
      and `childGoals`: a nested tree was tried first and qwen2.5:3b put a month under the wrong
      quarter. Tests: 521 backend (GoalCalendarTests, GoalServiceTests rewritten, GoalToolTests,
      TaskGoalRuleTests, GoalProgressCalculatorTests). Live on the dev API with qwen2.5:3b: API
      rules refuse Q3/September on 27 Sep and a task under a quarter; chat proposes a month with a
      "Nov 2026" card, refuses a task under a quarter and says to pick a month, refuses completing
      a month with open tasks and deleting a goal with children; `scripts/model-check.ps1` 8/8
      (its seed goal is now next month). Still true of the 3B model: it sometimes says a goal has
      no child goals when `childGoals` lists one.
      **Not deployed:** the web and phone still send and show the old goal shape (free dates,
      dropped, points in progress), so P1 ships together with P2. The live wipe runs then.
- [x] **P2. Goals UI (web + phone)** (2026-09-27). Web: the goals page is a tree — each child
      goal's lane indented under its parent with a line down its edge, standalone goals at their own
      level; lanes show key, title, slot ("Q4 2026"), dates, Overdue/Completed and progress read as
      "1 of 3 months done" or "1 of 2 tasks done" (never points). Monthly goals carry tasks and
      Create task; years and quarters offer "Add quarter" / "Add month". The menu has Update progress
      (only a goal with no tasks or children), Add quarter/month, Move, Complete, Reopen, Delete;
      Complete and Delete are shown disabled with the reason when they cannot be used (open tasks,
      active children, child goals to delete first). Show dropped and Drop are gone. Three dialogs in
      goals/: `goal-form-dialog` (New goal and Add quarter/month: type, year, start day for a year or
      quarter/month picker, Under; slots over, too short or taken are shown but disabled; the dates
      and day count preview live), `goal-move-dialog` (Under and slot, "–" detaches), and
      `goal-delete-dialog` (keep / move to other goals with a monthly-goal picker per task / delete).
      The goal page shows its quarters or months (with Add) or its tasks, Complete/Reopen with the
      reason, Type · slot, Dates and Under in Details, Move and Delete in the header, and a crumb to
      its parent. `core/goal-calendar.ts` mirrors the server's GoalCalendar (replaces goal-period.ts);
      `core/goals.service.ts` has the new shape, `move`, delete with task action, `inTreeOrder`. The
      task page's goal picker lists open monthly goals only; the backlog's unused goal fetch is gone.
      Phone: the same — tree with indent, slot chips, the same menu with disabled items that say why,
      `GoalSheet` (New goal / Add quarter / Add month), `MoveGoalDialog`, `DeleteGoalDialog`,
      `lib/goal_calendar.dart` (replaces goal_period.dart), API `createGoal` by slot, `moveGoal`,
      `deleteGoal` with task action (the phone's `_delete` now sends a body); the task sheet's goal
      picker lists open monthly goals. Selects never show a real choice as blank: "–" and "No goal"
      use a stand-in value, since a select shows null as empty. Checked in headless Chrome (desktop
      and 390 px, light and dark; dialogs measured, no overflow) and end to end against the dev API:
      Add month (Nov) under a quarter, then delete the October goal moving its task to November. The
      phone screens were rendered with real fonts in a throwaway widget test and looked at. An
      existing phone test caught `setState(() => _goals = …)` handing setState a Future. Tests: web
      99 (goal-calendar, goals page, form/delete dialogs, progress read-out), phone 86
      (goal_calendar_test, goals_tree_test), backend 521.
      **Deployed 2026-09-27 with P1 and the live wipe** (owner: "commit, deploy and wipe"): the API
      was stopped and the whole data volume copied to `data/backups/pre-goal-pivot-2026-09-27/`
      (now in .gitignore: it holds the Data Protection keys; integrity ok; 4 goals, 7 tasks, 2
      reminders, 10 conversations). The wipe ran on a copy first (integrity and foreign keys clean),
      then replaced the live DB, owned by the API's uid 1654: goals, tasks, sprints, comments,
      attachments, planner items, conversations and cards, documents and brief history deleted;
      reminders (links to goals and planner items cleared), settings, admin, devices and About you
      kept. There were no document or attachment files. `GoalHierarchy` applied on start. Live
      checks: goals and conversations empty, settings intact, a Q3 goal on 27 Sep refused with the
      reason, new web pages served. The volume's own nightly `backups/`, `pre-chat-wipe/` and
      `pre-publicid/` folders still hold older copies; delete them when the owner wants. Release
      arm64 APK built and sent to the owner.
- [x] **P3. Timeline (web + phone)** (2026-09-27). Web `/timeline` (nav: after Goals, gated by
      the goals module; `timeline/timeline.*`): one year as a Gantt chart — quarter and month axis
      (months at their true share of the year), month lines and today's red line behind every row,
      a label column (fold chevron, key link, title) and a bar per goal placed by its dates, filled
      to its progress, reading "Q4 2026 · 40%" (months just "40%": the bar sits under its month),
      completed green and overdue red, years tallest and months slimmest. Levels are by type (a
      standalone month sits at month level); parents come before their children; a goal with child
      goals folds, and Expand all / Collapse all does every one. The year dropdown offers the
      current year and every year with a goal. A bar opens the goal's page. Narrow screens keep
      the year at least 44rem wide and scroll it sideways under a pinned label column (titles on
      two lines, keys hidden there), starting a month before today; rows are placed by number
      because the line layer spans all of them. Phone `TimelineScreen` (Goals menu: Timeline): the
      same chart — pinned labels, 64 px months scrolled to a month before today, fold, year
      dropdown, a bar opens a sheet with the goal's type, slot, dates, progress, child goals and
      tasks (the phone has no goal pages). Checked in headless Chrome at 1280 and 390 px in both
      schemes and rendered on the phone with real fonts. Tests: web 104 (`timeline.spec.ts`), phone
      93 (`timeline_test.dart`). Deployed (web + APK) uncommitted, at the owner's word.
      **Swim lanes** (2026-09-27, owner: "each group should have a distinct swim lane; it looks
      jammed"): a top-level goal and everything under it now share one rounded band, with a gap
      between bands. The cause of the jam: levels are by type, so a standalone quarter ("Read 6
      books") sat indented right under a year's rows and read as part of it. Web: lane fill on the
      label and track cells (opaque, so the pinned labels still hide the scrolled year), month lines
      above the fill and under the bars, bars centred in their rows, and the see-through gap above
      a lane painted over in the pinned column. Phone: the same bands in both columns (see-through
      on the year side so the month lines show). Tests: web 105, phone 94.
      **Timeline becomes a tab of Goals; goals get lanes and folding** (2026-09-27, owner). Web: the
      board's tab frame is now a shared `shared/page-tabs.ts` (`section="board"` or `"goals"`,
      actions in `[pageActions]`); Goals has tabs Goals | Timeline, the Timeline lives at
      `/goals/timeline` (declared before `goals/:key`; `/timeline` redirects), and its menu item is
      gone. The goals page groups each top-level goal and everything under it in a swim lane (a
      grey band, goal cards on the page's surface so progress tracks and slot labels keep their
      contrast), and a goal with child goals folds, with Expand all / Collapse all. Tree, fold and
      lane logic is one set of helpers for both tabs (`treeRows`, `unfolded`, `byLane` in
      goals.service.ts). Phone: Goals has the same two tabs (`TimelineView` shares the Goals tab's
      load; tabs change by tapping so a sideways drag moves the year), the Timeline menu entry is
      gone, and the Goals tab has the same lanes and folding (`lib/goal_tree.dart`). Checked in
      headless Chrome (desktop and 390 px, both schemes) and rendered on the phone. Tests: web 109,
      phone 97.
- [x] **P4. Memory** (2026-09-29, **not committed or deployed**; migration `Memories` backfills
      the module on). `Memory` entity (content ≤500, category fact/preference/project/decision,
      source conversation, SetNull on delete); `MemoryService` (dedups the same sentence, keyword
      search with crude stemming, `RelevantAsync`: word matches first, then the latest 3
      preferences, 8 max); `/api/memories` CRUD + `/settings` (auto-save); `IChatContext` gives
      tools the conversation (chat turn and confirmed card). Tools `search_memories`,
      `create_memory`, `update_memory`, `delete_memory`; `IPersonaTool.NeedsConfirmation(config)`
      lets create/update skip the card while auto-save is on (receipt "Remembered — …"); delete
      always carded. `memory.prompty` carries the rules and the relevant memories.
      **Decision to confirm:** `remember_about_user` is now offered only while memory is off
      (`IPersonaTool.IsOffered`) — with both, qwen2.5:3b put preferences into About you. The PRD
      still lists both tools side by side. "I'll remember that" with nothing saved is a claim:
      `ActionClaimDetector.FindMemoryClaim` + a correction naming create_memory. Live (qwen2.5:3b):
      "please remember…" saves every time and `search_memories` answers "what do you remember";
      an implicit "I prefer…" saved 2 of 4 runs, and a saved preference was used in the next chat.
      Web: Memories card in Settings (search, category chips, add/edit/delete, auto-save toggle).
      Phone: Memories screen (module card), same features. Also fixed on the phone: four
      `setState(() => _x = future)` returned a Future, which asserts in debug builds.
- [x] **P5. Interaction modes** (2026-09-29, **not committed or deployed**; migration
      `ConversationMode`). `ChatModes` in Domain; `Conversation.Mode` kept per conversation and
      returned in list/detail; `POST /api/chat` takes `mode` (unknown = keep). Brainstorm/Reflect
      are offered no tool with `Mutates` (memory writes included); `OfferedToolGuard` refuses any
      tool not offered this turn (a write in Brainstorm no longer slips through as a card).
      `mode.prompty` (one section per mode, after the data). Plan: new `create_plan` tool — one
      card creates a goal tree with its tasks (`GoalService.ValidatePlanAsync` checks children
      against stand-ins with the dates they will get; max 16 goals / 40 tasks); several cards per
      reply already worked. Live: Brainstorm and Reflect change nothing; Act proposes at once;
      Plan used create_plan once in 4 runs, otherwise several create_goal/create_task cards
      (3B quality; one card had points 7, see below). Web: mode chips above the composer with a
      one-line hint. Phone: ChoiceChips above the composer. **Found, not fixed:** create_task
      accepted points 7 on a card (not Fibonacci) — check its ValidateAsync.
- [x] **P6. Speech services** (2026-09-29, **not committed or deployed**; migration
      `SpeechService`). Ports `ISpeechToText` / `ITextToSpeech`; adapter
      `OpenAiSpeechClient` (buffered bodies with Content-Length: a chunked upload read as empty on
      a simple server). Config on `InstanceConfig` (base URL, STT model, TTS model, voice, key
      under Data Protection purpose `PersonaOS.SpeechApiKey.v1`). `SpeechService` + `/api/speech`
      (status, admin PUT, admin test with a silent WAV, `transcribe` multipart ≤25 MB, `speak` →
      mp3; 409 when not set up, 502 with the service's reason). Setup Wizard step 6 (also adds
      the board and memory toggles it lacked). Web: Speech service card in Settings. Phone:
      `record` + `audioplayers` + `path_provider`; `ServerSpeech` records AAC 16 kHz mono, ends on
      a 2 s pause after speech (3 s hands-free), uploads, plays the reply; "Voice on this phone"
      switches in Settings; falls back to device speech when the server cannot. Verified end to end
      against a mock OpenAI-style server (test, speak, transcribe, bad key); **not yet tried with
      a real faster-whisper/Kokoro server or on a real phone**.
      2026-10-02: speech-to-text runs on a real faster-whisper server (`large-v3-turbo`
      int8_float16, about 1.2 GB of video memory); an AAC clip through `/api/speech/transcribe`
      came back word for word in under a second, and the owner confirmed voice on their phone.
      faster-whisper 1.2.1 needs PyAV below 15 (PyAV 19 dropped `metadata_errors`).
      Text-to-speech and shipping (same day, owner): both services ship in the repo as the opt-in
      compose profile `speech` (`COMPOSE_PROFILES=speech` in `deploy/.env`): `whisper` is built from
      `deploy/speech/whisper` (faster-whisper on the GPU, CUDA 12 from pip, non-root, healthcheck,
      model in the `speech-models` volume; it also passes `/v1/audio/speech` and `/v1/audio/voices`
      on to Kokoro because PersonaOS has one speech address) and `kokoro` is the upstream
      `kokoro-fastapi-gpu:v0.9.0-cu128` image. No host ports; live address is
      `http://whisper:8000/v1`, TTS model `kokoro`, voice `af_heart`. Off: remove the profile, stop
      the two, clear the address; or use OpenAI (README "Voice"). Natively Kokoro clashed (onnxruntime-gpu
      1.30 wants CUDA 13, Whisper CUDA 12), hence Docker. Checked live: test OK, transcribe 0.64 s,
      speak 0.26 s, stop/start of both gives a clear 502 and the phone falls back to its own voice.
      Video memory with the chat model: about 5.5 GB on this PC (qwen2.5:3b 2.7, Whisper 1.2,
      Kokoro 1, desktop 0.55): tight on the 6 GB RTX 3050; measure there, and if it does not fit,
      a smaller Whisper (`WHISPER_MODEL`) or Kokoro on the CPU. Not done: a CPU variant of the
      profile, and a phone fallback to its own speech-to-text when the server one fails.
      Owner's first phone run: recordings did not end on a pause. The fixed -38 dBFS "speech"
      line sat below the room's level once Android raised the gain, so only the 30 s limit ended
      it. Now `PauseDetector` (phone, `lib/pause_detector.dart`) learns the background and judges
      against it (speech +12 dB, silence within +6 dB, 2 s pause; 3 s hands-free; gives up after
      10 s with no speech); readings every 100 ms; the recorder uses the voice-recognition source
      with noise suppression and echo cancelling. Chat re-reads "Voice on this phone" each time
      the mic starts (it read it once per screen). Tests: `pause_detector_test.dart` (8).
      APK `PersonaOS-2026-10-02b-arm64.apk`; not yet confirmed on the owner's phone.
- [x] **Owner follow-ups (2026-10-01, deployed)**. P4–P6 deployed (live backup first:
      `data/backups/pre-p456-2026-10-01`); memory wins over About you (kept as built).
      - Cards refused up front instead of failing after Confirm: create/update_task check title,
        points (Fibonacci) and priority; create_sprint checks its dates; create_reminder runs the
        service checks, a time in the past included (`IReminderService.ValidateCreateAsync`).
      - Wrong password: the web put the message in a `mat-error`, which only shows for an invalid
        input, so nothing appeared; now a `role="alert"` line. Phone reads any 401 as a wrong
        password (an HTML/empty body used to say "could not reach the server").
      - Chat `9wuxkb2b`: the model worked "tomorrow" out as 2026-10-01 on 2026-09-29, then kept
        saying a reminder waited on a card when the call had been refused. The clock fragment now
        states the time, tomorrow and the next 7 dates; a refused change the reply still claims
        ends with "It could not be done: <reason>". Live replay: tomorrow right, past refused.
      - Tablet/landscape: phone board shows all columns side by side from 720 px wide
        (portrait tablet, landscape phone); list screens keep a readable 840 px width; the home
        screen goes two abreast from 640 px; `layout_sweep_test` pumps 8 screens at 4 sizes. Web
        board lanes now share the width (min 220 px) instead of a fixed 280 px, so a 1024 px
        landscape tablet shows all three.
      - Follow-up (owner: "timelines and goals page still not expanding in tab"): the phone
        Timeline had a fixed 64 px month and 128 px labels (~900 px total); months now share the
        width (min 48 px) and labels widen to 220 px when the year still fits. The phone Goals tab
        is no longer capped at 840 px. Web Timeline minimum lowered from 44rem to 38rem, so a
        1024 px landscape tablet with the nav open shows the whole year.
- [x] **Board tabs, quick views and full pages on the phone; goal quick view on the web**
      (2026-10-01, web deployed, APK sent). Phone board: Sprint | Backlog | Reports tabs like the
      web (`backlog_view.dart`: sprints and backlog, create/edit/start/complete/delete sprint, move
      tasks; `reports_view.dart`: sprint picker, numbers, burndown, tasks by status, velocity; the
      old report screen is gone). Phone task quick view gains description, comments
      (`comments_section.dart`) and Open full page (`TaskPage`); goal keys and the tasks under a
      goal open quick views, with a `GoalPage`; Timeline bars open the goal quick view. Fixed: a
      task sheet crashed when its sprint was not in the list it was given. Web: goal keys and
      Timeline bars open a goal quick view (`?goal=`); "Open full page" opens a new tab, which also
      ends a race where closing the dialog cancelled the page navigation on slow loads.
      Attachments reached the phone in the entry below.
- [x] **Remove the Documents module; bring every web feature the phone lacks to the phone**
      (2026-10-01; owner: "remove documents feature entirely and add features all to the mobile
      from web whichever is missing"). Live had 0 documents; backup taken first at
      `data/backups/pre-docs-removal-2026-10-01`, then deployed (migration `RemoveDocuments` drops
      the table and the `docs` feature key). Documents are gone from the API, Application, Domain,
      tools, prompt, web and phone. `IDocumentStorage` / `FileSystemDocumentStorage` and the
      `Documents:StoragePath` setting stay: attachments use them (renaming the setting would break
      existing installs). Phone gained: attachments on tasks and goals (pick, open, delete); task
      priority and created/updated line; goal edit (title, description, priority), complete/reopen;
      a sprint page (edit, start, complete, burndown, tasks) from the Backlog menu and the Reports
      tab; Reports search plus status and priority breakdowns, tasks open their quick view; Sprint
      tab menu with Open/Complete sprint; planner date picker and task/goal links; Settings for
      push (the two Firebase files), the speech service (test, save, remove key) and appearance
      (system/light/dark, kept on the phone); the update notice on home. APK
      `PersonaOS-2026-10-01e-arm64.apk`. Tests: backend 658, web 115, phone 187.

## AI layer refactor (raised by the owner 2026-09-21)

Six guardrails landed this week as incident responses — each a real bug, each the smallest fix
that worked, none followed by a refactor. The result: the prompt is 257 lines of `StringBuilder`
calls that need a rebuild and a redeploy to change one sentence, and `ChatService` is 545 lines
holding streaming, the tool loop, the confirmation gate, pre-proposal validation, call dedup, the
claim check, the corrective round and persistence. Adding a guardrail means editing that method
again. The layer boundaries are fine; the shape inside this layer is not.

Do these in order — A is the one the owner feels immediately, and B is easier once the prompt
stops being code. Estimates assume one focused session each, tests kept green throughout.

- [x] **A. Prompt lives in files, not in C#** (2026-09-23). Nine `.prompty` fragments
      (`identity`, `tool-rules`, `product`, `modules`, `clock`, `goals`, `board`, `planner`,
      `reminders`) under `Application/Ai/Prompts/Fragments`, embedded in the assembly. Front
      matter says what each is for; the body is a Mustache subset (`{{var}}`, `{{#list}}`,
      `{{^empty}}`, `{{! why }}`) rendered by `PromptTemplate`, about 200 lines with no new
      dependency. `SystemPromptBuilder` now only gathers values and picks fragments; user-written
      text (persona, about me) is appended raw, never rendered, so braces in it cannot inject tags.
      Overrides: a file of the same name in `Prompts:OverridePath` (compose:
      `/app/data/prompts`) replaces a default and is read per request, so edits apply without a
      restart; a broken override logs a warning and falls back to the default.
      `FilePromptOverrideSource` refuses any name that is not a plain `*.prompty`. Verified the
      new output byte-for-byte against the old builder (with and without data, thinking model
      and not) before deleting the comparison. Also: `InstanceConfig.IsEnabled` replaces six
      copies of `Features.TryGetValue(...) && on`. README: "Editing the assistant's
      instructions". 353 backend tests.
- [x] **A2. Fragments follow the Prompty format** (2026-10-01; owner: "format only"). Front
      matter of all 18 fragments now matches the Prompty spec (microsoft/prompty `spec/spec.md`
      §2): `template: {format: {kind: mustache}, parser: {kind: prompty}}`, and `inputs` as typed
      properties (`kind`, `description`, `example`) instead of one-line notes. Bodies are
      byte-for-byte unchanged and every rendered prompt is the same; the server still uses its own
      `PromptTemplate` and reads only `description`. No Prompty runtime or package, no `model`
      block (the model comes from Settings). Checked with PyYAML using the spec's split regex.
      Test `Every_shipped_fragment_follows_the_prompty_format`: the template block is there and
      the declared inputs equal the values the body uses (659 backend tests). README "Editing the
      assistant's instructions" says how to preview with the VS Code extension and the two
      differences from strict Mustache (no HTML escaping, the tag subset).
- [x] **B. Guardrails become a pipeline** (2026-09-23). `Application/Ai/Guards`. Tool calls
      pass `ToolCallPipeline`: `ArgumentEnvelopeGuard` -> `RepeatedCallGuard` ->
      `ConfirmationGate` (validates, then proposes); each lets a call through, rewrites it, or
      answers the model so it never runs. Replies pass `ReplyPipeline`: `ThinkingGuard` ->
      `LeakedToolCallGuard` -> `CorrectionPreambleGuard` -> `ClaimCheckGuard` -> `EmptyReplyGuard`.
      Shared per-turn state is `ChatTurnState` (answered calls, repeats, proposals, receipts).
      Every firing goes through `GuardTelemetry`: one log line and the
      `personaos.chat.guard.fired` counter (meter `PersonaOS.Chat`), tagged guard + model.
      `ChatService.StreamChatAsync` is orchestration only: history, `RunToolLoopAsync`, one
      review, at most one corrective round, `PersistAsync`. The 39 chat-loop tests passed
      unchanged; the rigs now share `TestChat.Create`. One behaviour change: a reply that was only
      reasoning now gets "I did not manage to write an answer", not the leaked-tool-call message.
      22 guard tests, including the metric. 375 backend tests.
- [x] **C. Model profiles** (2026-09-23). `Application/Ai/Models/ModelProfile.cs`: a profile per
      provider and model name (context size, thinks, tool-round budget, prompt variant, idle
      timeout), from the model-check scores. Small models (1.5B and under, read from the name)
      get `*.compact.prompty` fragments (`tool-rules`, `product`, `board`) and 4 tool rounds;
      Qwen3 is a thinking model; a context size set in Settings wins. New **`ollama` provider**:
      `OllamaMessageStreamer` speaks Ollama's own `/api/chat` and sends `think: false`,
      `options.num_ctx` and `keep_alive` from the profile (the OpenAI-compatible adapter cannot).
      The streaming port now takes one `AiRequest` with `AiModelOptions` instead of seven
      parameters. Settings (web, setup wizard, phone) offer Ollama first for local models; the
      web has a Context size field (`InstanceConfig.AiContextTokens`, migration
      `ModelProfilesAndSummaries`). Checked live against `qwen2.5:3b-instruct` on this PC:
      `get_goals` in 1.8 s with a 9.2k-token prompt, and a goal proposal became a card.
      `scripts/model-check.ps1` works with any provider (it switches only the model); no
      separate .sh was written.
- [x] Backend (2026-09-15). Goals no longer nest and get `GOAL-n` keys; `BoardTask` (`TASK-n`)
      and `Sprint` entities; migration `SprintBoard` turns sub-goals at any depth into tasks
      under their top-level goal (checked against a copy of the live DB first) and switches the
      `board` module on. `BoardService` owns the rules: lowest-free key numbers, Fibonacci
      points, scope changes refused without acknowledgement (`scope_change_unacknowledged`),
      and the weekly cycle (`RunCycleAsync`, ticked every minute by `SprintScheduleService`):
      close Sunday 18:00 with carry-over, 19:00 push nudge, auto-start 20:00, catch-up after
      downtime. The first week of a new board has no committed number, so filling it is not a
      scope change. Tools: `get_board`, `get_sprint_report`, `create_task`, `update_task`,
      `move_task`, `delete_task`, `delete_goal`; goal tools take keys (`link_goal` removed). The
      system prompt carries the sprint and planning guidance; the morning brief lists the
      week's open tasks. Planner items can link a task (`TaskId`). 228 tests.
- [x] Web (2026-09-15): `/board` with native HTML5 drag and drop plus a "Move…" menu,
      Current/Next, Start sprint, sprint report table, task dialog; goals page lists tasks with
      keys and points; planner "From the board…" picker and goal chips. **Not looked at in a
      browser** (signing in there means typing the password); build and API checked live.
- [x] Mobile (2026-09-15): `BoardScreen` — swipeable columns, long-press drag onto a drop bar,
      task sheet with point chips and Move to, sprint report; goals screen without sub-goals;
      planner picks from the board. Widget tests for drag, scope confirmation and the sheet
      (mutation-checked); layout checked from rendered screenshots, light and dark.
- [x] Follow-up (2026-09-16, owner: "I deleted task 1 from the board but not deleted"). A task
      that is done but in no sprint — how a completed sub-goal arrives from the migration —
      was labelled "Backlog" under its goal while never appearing on the board, so it could
      not be deleted anywhere. `BoardColumns.Of` now reads such a task as Done, and the goals
      page (web and mobile) can reopen or delete a task. Regression test
      `Work_finished_outside_a_sprint_reads_as_done_not_as_backlog` (mutation-checked).
      **Uncommitted, but deployed to the owner's server**; the phone needs a new build.
- [x] **Board changes round 2** (2026-09-16, owner's 13-point list). Sprints are now manual like
      Jira — create / start / complete / delete, with keys `SPRINT-n`, an optional name and
      editable start and end dates; nothing runs on a timer any more, only the Sunday 19:00 nudge
      (`RunRemindersAsync`, recorded as the `sprint_planning` proactive job). The board shows the
      running sprint's three columns only; the backlog moved to its own Jira-style page
      (`GET /api/board/plan`) with sprint sections, inline status / priority / points, drag and
      drop between sprints and a "Move…" menu. Tasks and goals gained **priority**, description,
      comments and attachments (`WorkItemComment` / `WorkItemAttachment`, stored beside documents,
      25 MB cap, `/api/items/{type}/{key}/…`). "Story points" are now **value points**. Every item
      has a URL: `/board/tasks/TASK-1`, `/board/sprints/SPRINT-1` (with a points-remaining chart
      and the work by column), `/board/goals/GOAL-1`. Tools: `get_plan`, `create_sprint`,
      `start_sprint`, `complete_sprint`, `add_comment`; `move_task` and `create_task` take sprint
      keys. Migrations `BoardPlanning` + `NormalizePriority` (the first shipped an empty priority
      default, caught on the live server and repaired). 238 backend tests, 61 mobile tests.
      The phone keeps the board working (sprint picker on a task); its own detail views, comments
      and attachments are the next round. Released as alpha.8 and deployed to the owner's server.
      The web pages were checked in a browser afterwards — see the entry below for what that found.
- [x] **Backlog is a view of the board, not a section** (2026-09-16, owner). The top navigation
      has one **Board** entry again; a `Sprint | Backlog` switch inside the page moves between
      them, and the backlog lives at `/board/backlog` (`/backlog` redirects). Found and fixed
      while finally looking at these pages in a browser: every `<select>` whose options come from
      `@for` showed its first option instead of the task's real value, so the backlog rows and the
      task page claimed "Highest / not estimated / no sprint" — they bind with `ngModel` now;
      comments and attachments never loaded on first paint (the parent called `load()` while the
      child was still hidden) — `Discussion` loads itself; every new comment was flagged *edited*
      because `CreatedAtUtc` and `UpdatedAtUtc` each ran their own `UtcNow`; and the app had no
      body background, so a browser in dark mode painted dark text on a dark canvas.
      239 backend tests. Committed as 1340b19 and deployed.
- [x] **`get_planner` refused a call with no date** (2026-09-21, seen live). "What are my tasks
      for today?" produced five `Provide 'date', or both 'from' and 'to'.` failures and then a
      date from 2023. An empty call now reads the user's local today; half a range is still
      refused, because "from" alone has no sensible reading.
- [x] **A model repeating one tool burned the turn and wrote nothing** (2026-09-21, seen live).
      After the `get_planner` fix above, the same question called it eight times with the right
      date and left the user an empty bubble. Repeat calls are now served from the first result
      with a note saying so, the loop stops after two repeats, and a turn that ran tools but
      produced no text gets a reply saying so instead of an empty bubble.
- [x] **Tool arguments arrived wrapped in an envelope** (2026-09-21, qwen3:4b on the owner's
      instance). The model sent
      `{"function":"add_planner_item","arguments":{"date":"2026-09-21","title":"Check tasks"}}`
      where the tool expects the arguments bare, so every field read as missing: the confirm card
      said only "Add planner item", and confirming it would have failed with "'date' is
      required." `ToolCallInput.Normalize` unwraps `arguments` / `parameters` / `args` / `input`
      (object or JSON string) where the rest of the object is only envelope keys, applied where
      tool calls enter the chat loop so the registry, the card, its summary and the repeat check
      all see the same fields. `add_planner_item` also validates its date before proposing.
- [x] **Qwen3 thinking: the three gaps** (2026-09-23). A model that sends nothing for its
      profile's idle timeout (60 s hosted, 180 s local, 300 s thinking) ends the turn with "The
      model stopped responding…" instead of a spinner; the wait restarts with every chunk
      (`TimeProvider`-driven, tested with `FakeTimeProvider`). `<think>` blocks were already
      stripped (`ThinkingGuard`). A prompt larger than the model's context is logged as a warning
      naming the fix (Settings, Context size), and history is budgeted so it does not add to it.
- [x] **False "nothing was saved" warning on a reply that read the board back** (2026-09-21).
      "All tasks are either in progress or have been scheduled for future planned sprints" is a
      description of existing state, but the passive rule read "have been scheduled" plus "tasks"
      as a claim. The passive rule — always the weakest signal — now loses to reporting wording:
      "as of", "currently", "already", "there are", "you have", "all/both/each/none of the X
      are". The first-person rule is unchanged.
- [x] **Scored the models instead of guessing** (2026-09-21). `scripts/model-check.ps1` runs eight
      scenarios against a running instance — read today's planner, read goals, read the board,
      refuse `GOAL-99`, propose a real deletion, propose a planner item with title and date,
      propose a reminder at the right time, and no false claim warning on an honest read — each
      in its own conversation, checking what the tools did rather than what the reply says. It
      discards every card it creates and restores the model it found. Results on the owner's box:

      | Model | Score | Per turn |
      |---|---|---|
      | `qwen2.5:0.5b` | 3/8 | under 3 s |
      | `qwen2.5:3b-instruct` | **8/8** | 0.6-7.7 s |
      | `qwen3:4b` | 7/8 | 8-76 s |

      So `qwen2.5:3b-instruct` is the floor that works, and 0.5B is not usable: it answered "what
      are my tasks today" from `get_goals`, replied "FOREVER" to another question, and proposed
      nothing where a card was wanted. qwen3:4b passes but is five to ten times slower and put
      21:00 in a reminder whose own text said 7 PM.

      Three app bugs the run turned up, all fixed: `get_planner` and `get_goals` both sounded like
      "tasks", so the descriptions now say which owns "what are my tasks today"; a reminder card
      could be proposed with no time at all, and `create_reminder` now validates `dueAtLocal`
      before proposing; and the gate's own message ("Tell them what you are proposing…") was
      phrased as instructions, which a 0.5B model repeated to the user word for word — it is a
      status object now.
- [x] **`/no_think` does not reach Qwen3 through the OpenAI-compatible endpoint** (2026-09-23).
      Settled by the Ollama provider, which sends `think: false`; the prompt switch stays for
      providers that read it.
- [x] **History budgeted by tokens** (2026-09-23). `HistoryPlanner`: the prompt, tools and new
      message are always sent whole, a quarter of the context (at most 2,048 tokens) is kept for
      the reply, and the newest messages that fit fill the rest. Fallback replies are left out.
      Up to 200 stored messages are considered.
- [x] **"I've created a goal called Learn Rust for you" above an unconfirmed card** (2026-09-23).
      `ClaimCheckGuard` now also runs when the turn proposed something, using
      `ActionClaimDetector.FindDoneClaim`: done-tense claims only, and never a sentence about the
      card itself (card, below, prepared, drafted, waiting, pending). It costs one corrective round,
      with its own instruction ("describe the change as proposed, not done; do not call the tool
      again"). No "nothing was saved" note in this case: the card is right there.
- [x] **A reply about the user's data that is not true** (2026-09-23). The "no read tool ran"
      heuristic stayed noisy: the prompt already carries today's goals, sprint and planner, so an
      answer from it is legitimate. Built the checkable half instead: `ItemReferenceGuard` looks up
      every GOAL-n, TASK-n and SPRINT-n the reply names, and the ones that do not exist are stored
      (`ChatMessage.UnknownItems`, migration `ChatMessageUnknownItems`) and shown under the reply
      on web and phone ("This reply mentions TASK-6, which does not exist"), spoken in hands-free.
      Invented keys are what small models were seen producing.
- [x] **"Reply yes to confirm" above a card with buttons** (2026-09-23). `CardInstructionGuard`,
      only when the turn proposed something: sentences telling the user to answer in words ("reply
      'yes'", "just say confirm", "(yes/no)", "yes or no") are replaced with "Use the Confirm
      or Discard button below." The word must follow the verb, so "tell me if you want no
      reminders" is left alone. The prompt rule now names yes and no too.
- [x] **Goals have a start and an end date** (2026-09-22, owner's rules). A goal starts on any
      day the user picks, future ones included, and its type bounds the end: a month runs at most
      31 days, a quarter at most 90, a year always ends on 31 December of its start year — both
      ends counted. With no end given it defaults to a month on less a day, 89 days on, or 31
      December. This replaces snapping every start to the 1st of its period, which left "Complete
      LLM Engineering Course by Oct 15th" filed as the whole of October with the real date in the
      title and nothing knowing when a goal was due. `GoalPeriodCalculator` holds the rules;
      `GoalService` enforces them on create and update for every caller, and refuses a missing
      start (it used to store 0001-01-01). `create_goal` takes `periodEnd`, checks the dates before
      the card is shown, and the card names the end. Migration `GoalPeriodEnd` backfills existing
      goals from their type. Web form has labelled Starts / Ends fields that reset the end when
      the type or start changes, lock it for a year, and explain a bad range before saving; goals
      show their range and an overdue badge. Phone: the same, with date pickers in the new-goal
      sheet. Both mirror the rules for instant feedback; the server stays the authority.
      307 backend tests, 74 mobile.
- [x] **Open questions from the goal-dates work, settled** (2026-09-23). Planner items still use
      ids in chat tools, but a confirmation card now names the item an id points at, looked up
      when it is proposed ("Update planner item status “Gym” on 2026-09-23 — done"), so a list
      position passed as an id is visible before Confirm; see the next item. The scope-change
      warning stays off for re-estimating mid-sprint, by design. Web drag and drop works on touch
      browsers since the move to CDK drag and drop.
- [x] **Reminder alarm rang a few minutes late — fixed in code, to confirm on the phone**
      (2026-09-23). The phone learnt about a new reminder only from the server's
      `reminder_scheduled` push. That is a data-only message, and FCM lowers the priority of
      data messages from an app that shows nothing for them, so on an idle phone it could arrive
      after the reminder was due: the alarm was never set and the server's due-time push rang
      instead, late. Once that one rang, the app counted as active again and later pushes came on
      time — the reported pattern, for phone- and server-created reminders alike. (Exact-alarm
      permission was ruled out: `USE_EXACT_ALARM` is declared and granted automatically.)
      The alarm no longer depends on that push: `PushService.resyncAlarms` brings the phone's
      alarms in line with the server right after the phone creates, cancels or deletes a
      reminder, after a reminder card is confirmed in chat, and whenever the app comes back to
      the foreground. A reminder made on the web while the phone sits untouched still relies on
      the push; opening the app sets it. Needs a new APK and a real-phone check.
- [x] **Sub-goal form should not ask for a period.** Settled by the sprint board: goals stopped
      nesting, sub-goals became tasks, and the child period picker went with them (web and phone).
      Original report below.
      On the phone, the "New sub-goal" sheet under a
      parent goal shows the Yearly / Quarterly / Monthly selector (it defaulted to Monthly under a
      yearly goal). The owner wants a sub-goal to **inherit the parent goal's timeline**, with no
      period choice at the child level: just the title and "Add goal". Check whether the web
      goals page has the same selector for sub-goals, and whether the server should enforce the
      inherited period (including for sub-goals the assistant creates in chat), not only the form.
- [x] **Confirming an update or delete fails, and hands-free voice stalls afterwards.**
      Fixed 2026-09-20, in four parts. (1) Tools now carry a `ValidateAsync` the registry runs
      *before* a mutating call becomes a card: a call naming something that does not exist is
      refused, the model is handed the reason as an error result while it can still correct
      itself, and the user never sees a card that cannot work. Implemented for the goal, board,
      sprint, work-item, planner and reminder tools. (2) `ToolReceiptBuilder` unwraps the
      registry's `{"error":"…"}`, so a failed card reads "Goal 5 does not exist." instead of raw
      JSON — web and phone both, since it is stored server-side. (3) The registry caught only
      `GoalValidationException`, so a board, planner or reminder problem reached the model as
      "The tool failed unexpectedly."; it catches `DomainValidationException` now. (4) The system
      prompt explains the card — Confirm and Discard buttons, never ask the user to say
      "confirm" — and the phone remembers that a card paused hands-free, resuming the loop when
      the last card is resolved (three widget tests; `VoiceService` is injectable for them).
      Checked live against the dev instance on qwen2.5: `Delete GOAL-99` produced no card and
      logged "Refused to propose delete_goal", `Delete GOAL-2` still produced one.
      Original report below.
      The owner
      tried to delete and update goals from the phone chat; tapping Confirm did not make the change.
      What the screenshot shows: the goals list numbered 1–5 (Create Tutorial was no. 4, Master AI
      no. 5). The user said "delete", then "create tutorial", then "yes". The proposal card
      resolved as failed with the **raw JSON** `✕ {"error":"Goal 5 does not exist."}`. So the
      model most likely passed a **list position instead of the real goal id** (and not even the
      right position).
      **Second example confirms it (progress update):** `get_goals` listed 1. Get Masters,
      2. Learn Rust, 3. Read Books Daily, 4. Master AI. "set progress for read books to 20%"
      produced a card that failed with `✕ {"error":"Goal 3 does not exist."}` — 3 is exactly
      Read Books Daily's position in the list. So the model uses the numbered position as the
      goal id. Things to fix and check:
      - The model needs real ids to act on. Check that `get_goals` returns ids and that the
        prompt or tool description tells it to use them, or let update/delete tools accept a
        title and resolve it.
      - A proposal whose target does not exist should be rejected when it is proposed, not
        after the user confirms it.
      - Show a readable failure instead of raw JSON on the card (mobile, and check web).
      - The same reply told the user to *say* "confirm" rather than tap the button. The model
        does not know how the confirmation card works; the prompt should say so.
      - Hands-free: the snackbar said "Hands-free paused — confirm the change first", and after
        the failed confirm the voice conversation never resumed. Resolving a card (confirmed,
        failed or discarded) should resume listening, or at least show how to resume.
- [x] **"Nothing was saved" warning fires on an honest reply, and the correction leaks into the
      reply.** Fixed 2026-09-20. `ActionClaimDetector` now separates "I've set" (done) from
      "I'll set" (intended): an intention counts only when the reply asks the user for nothing.
      "Sure, I'll add a new goal … Would you like to proceed with these details?" is a proposal
      and passes; "I'll create a sub-goal for that." on its own is still caught, which is why
      "I'll" is in there at all. Anything claimed as already done is flagged regardless of what
      else the reply asks. The corrective round's instruction now says to write only what the
      user should read and not to introduce the reply, and `CorrectionPreamble` strips a leftover
      "Sure, here is the corrected message:" lead-in. What the first draft said was not recovered
      from the logs — the correction round is only logged as a warning with the claim sentence.
      Original report below.
      reply.** Two problems with the claim check shipped in alpha.6, both seen on the phone:
      - **False positive.** "please add a goal to create a tutorial" got *"Sure, I'll add a new
        goal to create a tutorial. … Here's the goal: … Would you like to proceed with these
        details?"*. That is a request for permission, not a claim, but it was flagged with the
        amber note. `ActionClaimDetector` counts "I'll/I will + verb" as a claim. A future-tense
        sentence in a reply that ends by asking to proceed should not count. Decide whether "I'll"
        belongs in the detector at all (it was included because models say "I'll create…" and
        then never call the tool), and add this reply as a negative test.
      - **Correction leaks.** Another reply began *"Sure, here is the corrected message:"* before
        "Would you like to delete the 'Create Tutorial' goal?". The model is answering the hidden
        "[Automatic check]" turn as if talking to it. Strip that preamble, or reword the request so
        the model writes only the reply to the user. (It also means a correction round ran on a
        reply the user never saw as wrong; confirm what the first draft said, from the API logs.)
- [x] **Wrong task facts defended for turns (chat `xc498w5s`, 2026-09-27).** Built and tested
      2026-09-28, **not yet committed or deployed** (owner to say). qwen2.5:3b read
      `unestimatedCount: 0` in the sprint report as one unestimated task, pinned it on TASK-1
      (3 pts), then defended that from its own earlier replies without calling a tool.
      - `ItemFactGuard` checks each reply's claims against the DB: a task named by key or by a
        running-sprint title (points, unestimated, done / in progress / backlog), a sprint by key,
        and keyless task claims against the running sprint ("all tasks are unestimated", "none are
        estimated", "all 3 tasks have been completed", "Value: None"). Sprint sentences are read
        clause by clause; clauses with a modal or condition ("not allowed", "once all are done")
        are skipped. `ChatService` gives one correction round with the true facts; if still wrong,
        the wrong sentences are removed and "To be exact: …" appended.
      - `PromisedLookupGuard`: "let me check", "hold tight", "I don't have the details", "please
        give me the task key" with no tool run get one round to call the tool; the instruction
        names it (`ChatService.LookupToolFor`). That round's reply is used only if it ran a tool,
        or answers without another dodge and names no unknown items. A promise followed by a list
        is an introduction, not a promise.
      - `BoardTools.SprintForModel` says counts in words; two prompt rules: never ask for what a
        tool can tell you, and re-check when the user questions an earlier reply.
      - Replays of the owner's five messages (4 runs, local Ollama): every wrong estimate claim
        corrected. `model-check.ps1`: 7/8, 8/8, 7/8, then 8/8 three times in a row (2026-09-28);
        the one named failure was "reads today" (answered "- None"), which passed on every rerun.
      - Still open: sprint-level column claims ("the tasks are in the backlog, not pulled into
        the sprint") are not checked; the model sometimes asks for a task key even when told
        which tool to call (the first reply then stands). 602 backend tests, 0 warnings.

---

## ✅ Phase 0 + 0.5 — Foundation (done 2026-07-19)

- [x] Solution scaffold: `PersonaOS.Api` / `Domain` / `Infrastructure` (.NET 10, .slnx)
- [x] EF Core + SQL Server, `InstanceConfig` + `AdminUser`, auto-migrate on startup
- [x] JWT auth (`POST /api/auth/login`), single-admin model
- [x] `GET /api/branding` (nickname + enabled features + version contract)
- [x] Setup Wizard backend (`GET /api/setup/status`, one-shot `POST /api/setup`, authed `PUT`)
- [x] `RequireFeatureAttribute` feature-toggle guard (functional test pending first gated module)
- [x] Angular 20 dashboard skeleton + Setup Wizard UI (browser-verified)
- [x] Flutter skeleton: server-URL entry → branding fetch
- [x] API-key encryption at rest (Data Protection), CVE-pinned transitive packages

## ✅ Phase 1 — Chat (done 2026-07-20)

- [x] `Conversation` / `ChatMessage` / `UserProfile` entities + migration
- [x] `ChatService` on official Anthropic C# SDK (streaming, history window 20, usage capture)
- [x] SSE `POST /api/chat` (start/delta/done/error) + conversation history endpoints
- [x] Flutter login + streaming chat screens
- [x] **Provider-neutral AI** — the app is no longer Claude-only. `OpenAiCompatibleMessageStreamer`
      (thin `HttpClient` + SSE) sits behind the existing `IAiMessageStreamer` port alongside
      `AnthropicMessageStreamer`; `AiMessageStreamerFactory` picks one per `InstanceConfig.AiProvider`.
      One OpenAI Chat Completions adapter covers OpenAI, **local Ollama (free/keyless)**, Groq,
      OpenRouter, LM Studio, etc. via `AiBaseUrl`. Migration `MultiProviderAi` renames
      `ClaudeModel`→`AiModel` (value preserved) and adds `AiProvider`/`AiBaseUrl`; the encrypted-key
      column + DP purpose are untouched. Setup Wizard + Settings expose provider/base-URL/model/key;
      Anthropic requires a key, OpenAI-compatible may be keyless. Backend + web build 0-warning,
      46 tests green. **Verified live against local Ollama** (`qwen2.5:latest`, keyless): a real
      reply streamed, and the full native tool-use loop fired — model called `create_goal`, the
      adapter assembled the streamed tool call, `ChatService` dispatched it, and the goal actually
      persisted to the DB. Free, no Anthropic credits.
- [x] **Live smoke test — DONE (free, via Ollama).** The blocker was Anthropic *credits*, not code:
      the Anthropic path authenticated a real key and reached the API; the completion + native
      tool-use loop is now proven end-to-end against a local OpenAI-compatible model instead.
- [x] **Rolling summaries of older turns** (2026-09-23). Messages leaving the window are folded
      into `Conversation.Summary` by `ConversationSummarizer` (one model call, `summarize` and
      `summarize-input` fragments, 1,200 characters at most) and appended to the system prompt
      (`conversation-summary`). When a summary is needed the window is cut to 60% of its room,
      so one summary serves several turns. A failed or slow summary never blocks the reply.
- [x] **"About you": the user profile gets an API, a screen and a tool** (2026-09-23).
      `UserProfileService` + `GET/PUT /api/profile`; 2,000 characters at most, since it is sent
      with every message. Web Settings and the phone's settings have an "About you" field, saved
      only when it changed. New `remember_about_user` tool: "remember that I'm vegetarian" adds
      the fact on its own line (never twice), behind a card like every write.

## Phase 2 — Goals (Y/Q/M)

- [x] `Goals` table: self-referencing hierarchy (`ParentGoalId`), period type, status, progress
      — entity `Goal.cs`, migration `20260721054352_Goals`, applied to dev DB.
- [x] Progress rollup in domain service (parent derives from children)
      — `Domain/Services/GoalProgressCalculator.cs` (rounded avg of non-dropped children;
      leaf falls back to own progress). Verified: avg(80,20)=50; dropping a child re-rolls to 80.
- [x] REST CRUD endpoints (`/api/goals`) behind `RequireFeature("goals")`
      — `GoalsController` (GET tree/one, POST, PUT, PUT /status, PUT /parent, DELETE subtree).
      `GoalValidationException` → 400 via `GoalValidationExceptionFilter`.
- [x] **Tool registry** in `ChatService` (`IPersonaTool`: name/description/schema/execute)
      — `Ai/Tools/IPersonaTool.cs` + `PersonaToolRegistry` (feature-gated, never throws to model).
      `ChatService` now runs a tool loop (max 8 iterations) and emits an SSE `tool` event.
- [x] First Claude tools: `get_goals`, `create_goal`, `update_goal_status`, `link_goal`
      — `Goals/Tools/GoalTools.cs`; all 4 JSON schemas validated. **End-to-end tool call still
      needs the live-key smoke test** (blocked on owner's API key — same blocker as Phase 1).
- [x] Feature-toggle functional test (disable goals → 403 + hidden in branding)
      — verified live: disabling `goals` drops it from `/api/branding` and returns 403
      `feature_disabled` on `/api/goals`; re-enabling restores 200.
- [x] Flutter Goals screen (hierarchy view) — indented tree, rollup bars, add/sub-goal/
      set-progress/complete/drop/delete. `flutter analyze` clean, `flutter test` green.
- [x] Dashboard Goals view — shipped in Phase 7 (flattened tree, rollup bars, add/complete/drop/delete).

## Phase 3 — Daily Planner

- [x] `PlannerItems` table (date, task, optional Goal link)
      — entity `PlannerItem.cs` (+ `PlannerItemStatuses` planned/done/skipped, optional
      `ScheduledTime`, `SortOrder`), migration `20260721132154_PlannerItems`, applied to dev DB.
      Goal FK is `OnDelete(SetNull)`: deleting a goal keeps its tasks and unlinks them (verified).
- [x] REST CRUD + Claude tools (mirror Phase 2 pattern)
      — `PlannerService` + `PlannerController` (`GET /api/planner?date=|from=&to=`,
      POST/PUT/DELETE `/items`, `PUT /items/{id}/status`, `PUT /items/{id}/date`) behind
      `RequireFeature("planner")`. Tools: `get_planner`, `add_planner_item`,
      `update_planner_item_status`, `move_planner_item`.
      Verified live: create (timed + linked), day/range query, ordering (timed before
      unscheduled), status, move-to-tomorrow, 400s for bad status / missing goal,
      feature toggle 403 + hidden in branding.
      **Note:** refactored `GoalValidationException` → shared `DomainValidationException`
      base + single `DomainValidationExceptionFilter`; goal error codes unchanged
      (`goal_validation_failed`). Subclass it for future modules — don't add a filter.
      System prompt now includes the user's local *today* + today's planner items.
      **End-to-end tool call still needs the live-key smoke test** (same blocker as Phases 1–2).
- [x] Flutter planner day view — date nav, add (optional time), status cycle, move-to-tomorrow, delete.
- [x] Dashboard planner view — shipped in Phase 7 (day nav, add, status cycle, move-to-tomorrow).

## Phase 4 — Reminders + FCM push

- [x] `Reminders` + `DeviceTokens` tables
      — `Reminder.cs` (UTC `DueAtUtc`, pending/delivered/cancelled/failed, attempt counter,
      optional Goal + PlannerItem links via `SetNull`) and `DeviceToken.cs` (unique token,
      android/ios). Migration `20260723052926_RemindersAndDevices`, applied to dev DB.
- [x] **Android toolchain installed, and the first APK actually builds (2026-09-11).**
      Per the owner's convention everything lives under `C:\Apps\SDK\`: `Java\jdk-17.0.20.1+1`
      (Temurin; `sdkmanager` needs a JDK and none was installed) and `Android\` with
      cmdline-tools 9862592, platform-tools, platforms;android-36, build-tools;36.0.0.
      `flutter config --android-sdk` / `--jdk-dir` point Flutter at them, and
      **`flutter doctor` now reports "No issues found!"** where the Android toolchain used to fail.
      `flutter build apk --release` produced **app-release.apk, 50.7 MB** — the first APK this
      project has ever produced.
      ⚠️ Licence acceptance **cannot** be done from the PowerShell tool: it runs non-interactive
      with stdin from null, so `sdkmanager --licenses` silently accepts nothing and the install
      then does nothing. Use the Bash tool: `yes | sdkmanager.bat --sdk_root=... --licenses`.
- [x] 🔴 **CRITICAL, found only by running a release build (2026-09-11): the mobile app had no
      network access at all.** Flutter puts `android.permission.INTERNET` in the *debug* and
      *profile* manifests only — never in `main`. So every **release** APK shipped with just
      `RECORD_AUDIO`, could not open a single socket, and would have failed for every self-hoster
      with "Could not reach a PersonaOS server at that address" no matter what they typed.
      Confirmed by `aapt2 dump permissions` on the built APK, and invisible until today because
      nobody had ever built and run a release build — `flutter analyze`, `flutter test` and even
      `flutter run` (debug) all pass happily, because debug *does* have the permission.
      Fixed by declaring INTERNET in `main/AndroidManifest.xml`, plus a
      `network_security_config.xml` allowing cleartext: PersonaOS is HTTP-by-design over a
      private network, and Android blocks cleartext from API 28, so that would have been the
      very next wall.
      Also made the failure diagnosable — `_connect` swallowed the exception (`catch (_)`) and
      showed one generic line, which is why a missing permission looked identical to a typo'd
      address. It now reports the actual reason.
- [x] **Confirmation cards verified on a real Android build (2026-09-11).** Emulator (Pixel 6,
      API 36) with `adb reverse tcp:8080 tcp:8080`, so the device reached the instance over the
      cable with **no change to network exposure** — `PERSONAOS_BIND` stayed on loopback.
      Setup → login → asked for a monthly goal → the card rendered
      `Create goal “Mobile Test” — month · 2026-09-01 · top-level` with Discard/Confirm, and the
      assistant said it was *proposing*, not claiming it was done. `/api/goals` held **only**
      "Master AI" at that point — nothing written. Tapping Confirm turned the card into
      `✓ Mobile Test — month · 2026-09-01 · active` and the goal appeared. Period normalisation
      also held (the 1st, not today's date).
      ⚠️ The APK is unsigned-for-release (debug signing) and is **not** on a phone yet: the stack
      still binds to `127.0.0.1`, so a device on the tailnet cannot reach it — set
      `PERSONAOS_BIND=100.90.80.20` first.
- [x] Firebase project setup — superseded by bring-your-own Firebase (2026-09-14): the owner
      uploads their own project's files in Settings, and push has worked on their phone since.
      iOS/APNs is out of scope while there is no iOS app.
- [x] **Alarm-style reminders + push fixes (2026-09-14).** Confirmed on the owner's phone: the alarm
      rang full screen. Follow-up found and fixed there: Dismiss/Snooze left the screen open
      (`maybePop()` honoured the alarm's own `PopScope(canPop: false)`; now `pop()`), and the
      confirm card lost its Confirm button (theme `Size.fromHeight` = infinite width in a Row).
      Original report: owner tested
      the push build on his phone: the reminder arrived ~29 s late, the notification header read
      "2032y", and he wants a prominent alarm rather than a notification. Causes: (1) the
      dispatcher polls every 30 s and Doze adds more, so a push sent at due time can never be
      punctual; (2) FirebaseAdmin 3.6's `AndroidNotification.EventTimestamp` is a NON-nullable
      `DateTime` that serialises `event_time: 0001-01-01T00:00:00Z` when unset — confirmed by
      reflection on the DLL — so Android dates the notification ~2000 years ago; (3) two
      concurrent `POST /api/devices` for one token raced the unique index → 500. Plan: the server
      pushes the reminder SCHEDULE (data-only) when a reminder is created/changed/cancelled, the
      phone sets an exact alarm-clock alarm that fires a full-screen alarm with looping sound and
      Dismiss/Snooze; the due-time push stays as a data-only fallback the phone ignores if it
      already raised that alarm. Alarms resync on sign-in, and survive reboot via the plugin's
      boot receiver.
      **Built and ✅ verified on the emulator 2026-09-14** (deployed to the live stack):
      server `ReminderAlarmPublisher` pushes `reminder_scheduled` / `reminder_removed` from all
      four `ReminderService` mutations (best-effort — a push outage never fails the reminder);
      the dispatcher's due push is now data-only `reminder_due`; `IPushSender.SendDataAsync`
      added; `EventTimestamp` set explicitly; `RegisterDeviceAsync` survives the insert race.
      App: `ReminderAlarms` (exact `alarmClock` schedules, UTC-based, state in shared prefs so
      background isolates work), `AlarmScreen` over the lock screen, Snooze 10 min / Dismiss,
      a background FCM handler, sync on sign-in that runs even with push unconfigured.
      Evidence: a reminder created via the API became an exact alarm on the device within 8 s
      (`origWhen` = due time, `window=0`, `exactAllowReason=policy_permission`); with the screen
      locked it rang **0.8 s after due** (audio focus 20:48:36.820, `USAGE_ALARM`, INSISTENT,
      importance 5) and the full-screen AlarmScreen showed — also when the app process was
      already alive. Snooze stopped the sound and scheduled exactly +10 min; Dismiss stopped it
      and scheduled nothing; cancelling a reminder removed its alarm; the fallback due push
      woke the background handler and produced no second alert; a single `POST /api/devices`
      per sign-in (the race is gone). Backend 0 warnings, 153 tests; mobile 37 tests.
      **Caught in own code before shipping:** `PushService` re-initialised the notification
      plugin, which would have replaced the alarm engine's callbacks and silently broken
      Snooze, Dismiss and opening an alarm — removed.
      **NOT verified:** the owner's real phone (APK sent); alarms restored after a REBOOT (boot
      receiver is wired but was not exercised); the Android 14+ full-screen permission was
      already granted on the emulator, so the settings prompt path was not seen.
      **Heads-up:** the previous test build on the phone does not understand data-only reminder
      messages, so it gets no reminders from this server until updated. Status-bar alarm icon
      still uses the launcher icon (renders as a white blob) until real artwork exists.
- [x] FCM push, end to end (2026-09-14; confirmed on the owner's phone). The owner chose FCM over a
      self-hosted distributor (would have needed the separate ntfy app on the phone) and over
      server-synced local alarms. Found while planning: **the mobile app has no FCM code at all** —
      the "device-token registration" item below is backend-only — and **device registration is
      gated behind the `reminders` feature**, so turning reminders off also silently stops
      proactive briefs, which push to the same tokens. Plan: `FcmPushSender` adapter selected
      only when a credentials file is present (falls back to `NullPushSender`); move device
      endpoints to an ungated `/api/devices`; mobile registers its token on login and on refresh,
      shows foreground notifications, and builds cleanly WITHOUT `google-services.json` so the
      open-source repo stays buildable. Credentials stay out of git and out of the image.
      **Redesigned the same day to bring-your-own FCM, configured in the UI** (owner's call): the
      release APK must not carry any Firebase config, and other self-hosters must be able to use
      their own Firebase project without rebuilding the app. The first cut — a key file mounted
      from `deploy/secrets/` plus `google-services.json` compiled in via a conditional Gradle
      plugin — was never committed and has been removed.
      **How it works now:** the admin uploads BOTH Firebase files on the web Settings page
      (`PushConfig` component → `PUT /api/setup/push`, admin-only). `FirebaseConfigParser`
      validates them — refuses swapped files, files from different projects, and a
      google-services.json with no `com.personaos.personaos_mobile` app. The service-account
      key is encrypted under its own Data Protection purpose `PersonaOS.FcmServiceAccount.v1`
      (keyed `ISecretProtector`; never the AI key's purpose) and is never returned by any
      endpoint. The public client options go to the app via `GET /api/devices/push-config`, and
      `PushService` starts Firebase at runtime with them — so a reinstall recovers push on sign-in.
      `ConfigurablePushSender` swaps the live sender without a restart; `PushConfigLoader` restores
      it at startup. `NullPushSender` is deleted. Migration `FcmPushConfig` adds three nullable
      columns (safe for existing rows).
      **Ordering matters:** `SetAsync` loads the key into the live sender BEFORE saving it, so a
      well-formed file with an unparseable private key is rejected with nothing stored.
      **Verified:** backend 0 warnings, 145 tests (15 new); mobile analyze clean, 32 tests; web
      image builds; migration applied to the live DB. Live against the real Firebase SDK:
      swapped files, mismatched projects, and a fake private key each return a specific 400,
      and status stays `configured: false`. Also still true from the first cut: registration
      returns 204 with reminders disabled (was 403).
      **✅ Verified END TO END on the emulator, 2026-09-14, with the owner's real Firebase project
     ** uploaded through the web page: sign-in → app fetched the client
      options → `Firebase.initializeApp(options:)` started Firebase with NO google-services.json
      in the APK → notification permission granted → token registered → the dispatcher sent a
      queued reminder → notification shown (the reminder text, channel `reminders`, importance HIGH).
      Reminder 6 flipped to `delivered` at 13:04:30 UTC, the same second the device logged the
      FCM receipt. Before any device registered, the dispatcher correctly held the reminder
      ("no devices are registered") rather than consuming it.
      **Still to do:** (1) the owner's real phone — it runs alpha.4, which has no push code, so it
      needs a new build; (2) **the notification shows the app as `personaos_mobile` with the
      default Flutter icon** — `android:label` and the launcher icon were never set, which breaks
      the fixed "PersonaOS" brand rule; (3) the emulator's token is still registered, so reminders
      now go to it as well as the phone until it is removed or the app is uninstalled there
      (FCM then reports it `Unregistered` and the dispatcher prunes it).
      Decisions worth knowing: `MulticastMessage.Tokens` is obsolete in FirebaseAdmin 3.6 in favour
      of FIDs, but firebase_messaging 16.6.0 has no FID API, so tokens stay behind a scoped
      `#pragma` — move to `Fids` when the client can register by FID. Only `Unregistered` /
      `SenderIdMismatch` prune a token; `InvalidArgument` does not, since it can mean a bad
      payload and pruning on it would delete every device at once. Changing Firebase project
      while the app runs needs an app restart — Firebase cannot re-initialise its default app.
      **Next:** owner uploads both files in Settings, signs in on the phone, sends a reminder.
      **Open product question:** this BYO path serves technical self-hosters. A project-run push
      relay (or UnifiedPush as an opt-in) is what would serve everyone else — undecided.
- [x] Device-token registration on login; server hosted-service sends push at due time
      — `POST/DELETE /api/reminders/devices` (idempotent per token, verified); dispatcher
      `ReminderDispatcher` + `ReminderDispatchService` (30s `PeriodicTimer`, per-pass scope,
      never crashes the host). Prunes provider-rejected tokens; gives up after 5 attempts.
      Verified: with push unconfigured, due reminders stay **pending** (not failed) so they
      fire once FCM is added; cancelled reminders are skipped.
      ✅ The success path (mark delivered / prune invalid token / retry-then-fail) is now
      covered by `ReminderDispatcherTests` with a fake `IPushSender`.
- [x] Claude tool: conversational reminder creation (resolve relative times vs configured TZ)
      — tools `get_reminders`, `create_reminder`, `cancel_reminder`. `create_reminder` takes
      `dueAtLocal` (user wall-clock); the system prompt supplies their zone + today's date so
      the model resolves "tomorrow 9am" itself. Verified: 09:00 Asia/Calcutta → 03:30 UTC and
      back. New shared `UserClock` helper handles conversion incl. DST gaps.
      System prompt now also lists the next 5 upcoming reminders.
      **End-to-end tool call still needs the live-key smoke test** (same blocker as Phases 1–3).
- [x] Flutter Reminders screen (list, create with date+time picker, cancel, delete).
- [x] `flutter_local_notifications` for foreground display — already done:
      `PushService._showInForeground` shows FCM notifications while the app is open, on the
      "Reminders and briefs" channel, and reminder data goes to the alarms. Closed 2026-09-23.
- [x] `POST/GET /api/documents` — filesystem storage under `data/docs-storage/`
      — `Document` entity + migration `20260723055854_Documents`; `DocumentService` +
      `DocumentsController` (list/search, metadata, download, upload multipart, edit
      description, delete) behind `RequireFeature("docs")`.
      `IDocumentStorage` port → `FileSystemDocumentStorage` (root from
      `Documents:StoragePath`, default `<app>/data/docs-storage`).
      **Security:** on-disk names are server-generated GUIDs, user names are only metadata,
      and every resolved path is checked to stay inside the root. Verified: a filename of
      `../../../../etc/passwd` collapses to `passwd`; 25 MB cap; empty upload rejected;
      delete removes both the row and the file.
- [x] Claude tool `get_document(name)` (retrieval by name; NO RAG in v1)
      — shipped as `list_documents` + `read_document` (accepts `documentId` **or**
      `fileName`, disambiguating multiple matches). Text formats only (txt/md/csv/json/
      xml/code); PDFs/images/Office return a clear "cannot be read as text" message —
      extraction stays out of scope per the PRD. Reads truncate at 20k characters.
      **End-to-end tool call still needs the live-key smoke test** (same blocker as Phases 1–4).

## Phase 6 — Voice (push-to-talk)

- [x] Flutter mic button → `speech_to_text` → send as chat message. Push-to-talk: partial
      results fill the composer live; on the final result it auto-sends. Gated on the `voice`
      feature. `VoiceService` wraps both plugins; Android `RECORD_AUDIO` + recognizer `<queries>`
      and iOS mic/speech `Info.plist` keys added.
- [x] `flutter_tts` read-back toggle — AppBar toggle; when on, speaks each completed reply.
      **Not runnable headless here** — mic capture + TTS audio need a device/emulator; verified
      by `flutter analyze` (clean) + `flutter test` (green). Manual device check still pending.
- [x] Voice input failed silently — fixed in `ba4dbf3`, and **confirmed working on a real device
      on 2026-09-12**: dictation transcribes, no mic errors. The emulator still cannot transcribe
      (no SODA language pack), so test voice on hardware, never on the AVD.
- [x] Fingerprint sign-in (2026-09-13). Verified on the emulator with an enrolled print: the
      biometric prompt fires on cold start and one touch reaches chat with no password typed.
      Credentials go in `EncryptedSharedPreferences` (Keystore-backed) and are read only after
      the prompt passes. Chosen over storing the JWT because the access token lasts 12h and there
      is no refresh endpoint, so a token alone would still mean typing a password daily. Revisit
      if refresh tokens ever land — that removes the password from the phone entirely.
- [x] Mobile parity + modern UI + hands-free voice (2026-09-12). The
      owner tested the alpha on a real phone and asked for four things: a more modern interface,
      a hands-free voice mode rather than push-to-talk, chat history, and every feature the web
      app has. Gaps found by inventory — mobile has chat, goals, planner, reminders, login; the
      web also has conversation history (list/open/delete), documents, and settings. The backend
      endpoints for all three already exist, so this is client work only.
- [x] Voice input fails silently (emulator, 2026-09). Closed 2026-09-23: superseded by the
      hands-free voice work of 2026-09-12, which surfaces recognition errors and resets the mic;
      the original report is in git history.
- [x] Dashboard chat (reuse SSE endpoint) — `ChatService` reads the SSE stream with
      `fetch` + `ReadableStream` (not `EventSource`, which cannot send the bearer header).
      Conversation sidebar, streaming bubbles, per-call tool indicator, Enter-to-send.
- [x] Goals / Planner / Reminders views — all three verified against a live API in the browser.
      Auth is signal-based (`AuthService` + `authInterceptor` + `authGuard`); token in
      localStorage, 401 → `sessionExpired()`.
- [x] Settings page (edit nickname/persona/model/key — wraps `PUT /api/setup`).
      Needed a new **admin-only `GET /api/setup`** to prefill it; it returns
      `HasAnthropicApiKey` rather than the key — the key is never sent to a client.
      Saving refreshes `/api/branding`, so toggling a module updates the nav immediately.

**Gotcha for anyone doing dates on the frontend:** `new Date().toISOString().slice(0,10)`
is wrong for calendar days — it converts to UTC first, so east of Greenwich local midnight
lands on the previous UTC day and every date silently shifts back one. This shipped a bug
where "next day" and "move to tomorrow" both resolved to the *current* day. Use
`core/local-date.ts` (`todayLocal` / `shiftLocalDate`) instead.

- [x] Documents view (list, search, upload, download, delete), gated on the `docs` module.
      Download goes through `HttpClient` (responseType blob) so the bearer interceptor runs —
      a plain `<a href>` can't carry the Authorization header. Verified E2E: upload 201,
      authed download 200, search filter, delete 204.
- [x] "Update available" banner (`UpdatesService` + a dismissible banner in the app shell).
      Notify-only: checks the GitHub Releases API for `personaos/personaos`, compares
      `tag_name` against `/api/branding` `apiVersion` with a dotted-numeric `isNewer`, shows the
      `docker compose pull && up -d` command + a release-notes link. **Fail-silent** — a private/
      absent repo (currently a real 404), rate-limit, or offline just means no banner. Dismissal
      is remembered per-version in localStorage. Verified both paths in the browser (real 404 →
      no banner; injected newer release → banner renders; dismiss clears + persists).

Phase 7 dashboard parity is complete. Remaining across the project: **Phase 6 (Flutter voice)**,
plus the Flutter Goals/Planner/Reminders screens, and the live-key chat smoke test (blocked on a
real Anthropic key).

## Phase 8 — Proactive scheduler (feature-toggled)

- [x] Background scheduler (`ProactiveScheduleService`, 5-min `PeriodicTimer`, scoped per pass,
      survives failures). No Quartz needed — `ProactiveJobRun` + a unique index on
      (JobName, LocalDate) is what makes it idempotent, not the timer.
      `ProactiveService` takes a **`TimeProvider`** so scheduling is unit-testable;
      DI registers `TimeProvider.System`.
- [x] Morning brief + evening rollup (goal nudges included in the brief)
      — `ProactiveBriefComposer` builds text **deterministically** from planner/reminders/goals:
      no API call, so a quota or key problem can never silence the briefs.
      Scheduled per-job at local times on `InstanceConfig` (`MorningBriefTime` 07:30,
      `EveningRollupTime` 21:00; null disables that job). A job more than 3h late is skipped
      rather than fired stale after downtime. Silent when there's nothing to report.
      Each brief is pushed **and** filed as a chat conversation, so the user can reply
      ("yes, move them to tomorrow") with full context.
      `GET /api/proactive/runs` (audit) and `POST /api/proactive/run/{job}?force=` (manual)
      behind `RequireFeature("proactive")`, which is **off by default**.
      Verified live: 403 when disabled, real brief from planner+goals, `force=false` skipped
      as "already ran today", rollup with open items, unknown job 400, audit trail, and both
      briefs appearing in chat history. 11 unit tests cover schedule/idempotency/edge cases.
- [x] **Model-written phrasing for briefs** (2026-09-23). Off by default; Settings (web, under
      Modules, when proactive is on) has "Let <nickname> word the briefs"
      (`InstanceConfig.PhraseBriefs`, migration `PhraseBriefs`). `BriefPhraser` hands the composed
      brief to the model with the `brief-phrasing` fragment and uses the result only if every
      bullet's title and every time and number of the original is in it and it is not three
      times as long; any failure, a 60-second timeout or a changed fact sends the composed text.
      Live with qwen2.5:3b: one of three rewordings accepted, two refused and sent as composed.
      Found on the way: forcing a second run of a brief the same day failed with a 500 (the run
      record is unique per job and day) after the brief had already gone out; the record is now
      updated instead. Also fixed a flaky metric test (a shared static meter written from
      parallel tests).

## Phase 9 — Packaging + open-source release

- [x] Dockerfiles + `deploy/docker-compose.yml` (API + SQL Server + dashboard) + `.env.example`
      — API Dockerfile (multi-stage, non-root, restore-layer cached, data dirs pre-chowned);
      dashboard Dockerfile (Angular → nginx) with `nginx.conf` that proxies `/api`, disables
      buffering on `/api/chat` (SSE would otherwise be held until the reply completed), and
      caches hashed assets while marking `index.html` no-store.
      Compose: **only the dashboard publishes a host port, bound to 127.0.0.1 by default**;
      API and SQL Server are internal-only. DB healthcheck gates API start. Named volumes for
      the database and for `api-data` (documents + Data Protection keyring).
      `docker-compose.build.yml` overlay builds from source instead of pulling.
      ✅ **Both images build and the full stack runs** (verified 2026-07-24, Docker 29.6.2):
      API and dashboard images built first try; `docker compose up -d` brought the stack up
      with the DB healthcheck correctly gating API start.
- [x] `LICENSE` (Apache-2.0) + `NOTICE` + `README` + `CONTRIBUTING`
      — NOTICE spells out that the licence covers the code, not the PersonaOS name/logo.
      README is written for a self-hoster (install, Tailscale, update, where your data lives,
      what happens if you lose the keyring volume). CONTRIBUTING documents the layer rules,
      the ports pattern, the zero-warning bar, and the `TimeProvider`/fakes testing conventions.
- [x] Release pipeline: tag → GHCR images (`:x.y.z` + `:latest`) → GitHub Release (compose + APK)
      — `.github/workflows/release.yml` (tag `v*`), plus `ci.yml` running backend build
      (`-warnaserror`) + tests, dashboard build, Flutter analyze/test, and an image build.
      YAML validated locally; **not yet executed** (needs a GitHub remote).
- [x] In-dashboard "update available" banner (GitHub Releases API; notify-only) — shipped in Phase 7
      (`UpdatesService` + dismissible banner; fail-silent; dotted-numeric version compare).
- [x] **Build and run the container images** — done, no fixes needed.
- [x] Clean-machine install test; upgrade test (data survives)
      — **Install:** empty volumes → all 7 migrations applied automatically on first boot →
      Setup Wizard → login → goals/planner/documents created through nginx. Dashboard renders
      "Friday is ready" from the container. SSE chat streams **through nginx** as separate
      frames (proves `proxy_buffering off` on `/api/chat` works). Uploads land in the
      `api-data` volume under a GUID name; API runs **non-root** (uid 1654).
      Port exposure confirmed: only `dashboard` publishes, on `127.0.0.1:8080`; API and
      SQL Server are internal-only.
      **Upgrade:** `up -d --force-recreate` (volumes kept) → second boot applied
      *no* migrations ("already up to date") → nickname, admin login, goals, planner and
      documents all intact → **the encrypted Anthropic key still decrypted**, proving the
      Data Protection keyring survived (the failure mode that would silently force re-entry).
      `down -v` removes both volumes cleanly.
      ⚠️ Not yet tested: a genuine **N → N+1** upgrade across two different image versions
      (needs a published prior release) and the `minSupportedClient` version gate.
- [x] **Full E2E pass DONE 2026-09-11** — clean install *and* restart-with-volumes, on the SQLite
      stack. Run against an **isolated instance** (`docker compose -p personaos-e2e`, port 8081,
      own volumes) so the owner's live instance was never touched; torn down with `down -v` after.
      **Install:** empty volumes → 3 migrations applied → WAL + `synchronous=NORMAL` → integrity
      check → nightly backup written → singleton config seeded. Only `web` publishes a port.
      **Through the real UI:** Setup Wizard (provider switched to OpenAI-compatible, which
      revealed the Base URL field) → login **as `E2E-Admin` for the stored `e2e-admin`**, proving
      the case-insensitive fix end to end → chat → `add_planner_item` fired and the receipt
      rendered inline under the reply.
      **Modules:** goals rollup `effectiveProgress` 40 = avg(0,80); planner status cycle;
      documents upload/list/**authed download 200 / unauthenticated 401**/delete removes row+file;
      feature toggle → hidden in `/api/branding` + 403 `feature_disabled` → re-enable restores 200.
      **Path traversal properly defended:** a hand-built multipart upload with filename
      `../../../../etc/passwd` stored metadata `passwd` only, wrote a GUID inside `docs-storage`,
      escaped nothing, and left the container's real `/etc/passwd` untouched.
      **Restart with volumes kept:** *zero* migrations on second boot, integrity passed, and
      nickname/admin/planner/goals/reminder/document/conversations **and both tool receipts** all
      survived.
      ✅ **Keyring gap CLOSED** — the thing earlier runs could not test because the instance was
      keyless. Set a provider key (plaintext absent from `personaos.db`, keyring file present),
      recreated the containers, and after restart `POST /api/setup/test` returned
      `{"ok":true,...}` — i.e. the stored key still *decrypts*, not merely "is present". Zero
      crypto errors in the log.
      ⚠️ Still untested: a genuine **N → N+1** across two published image versions, and the
      `minSupportedClient` gate. Both need a release, which is deliberately not being cut yet.
      ⚠️ During teardown the owner's own containers were removed too (cause unclear — the `down -v`
      targeted only the `personaos-e2e` project). No data lost: `personaos_api-data` survived and
      `up -d` restored everything. If running a second project again, check `docker ps` afterwards.
- [-] ~~Re-run the clean-machine install + upgrade test on the SQLite stack.~~ Superseded by the
      entry above. Original note: the verification
      recorded above was performed against the old **SQL Server** compose stack (before `5ae9092`),
      so it is no longer evidence for what ships today: there is no DB container or healthcheck
      any more, and the database now lives inside the `api-data` volume alongside the Data
      Protection keyring. The keyring-survives-upgrade check in particular is worth repeating,
      since a lost keyring silently forces re-entry of the provider API key.
      ✅ **Install half DONE on the SQLite stack (2026-09-05, Docker 29.7.2).** Built both images
      from source (`-f docker-compose.yml -f docker-compose.build.yml up -d --build`) into empty
      volumes: `InitialCreate` applied, WAL + `synchronous=NORMAL` set, integrity check passed,
      nightly backup written, singleton config seeded pre-serving. Drove the **real UI** at
      `127.0.0.1:8080` through nginx: Setup Wizard → login → chat. Verified only `web` publishes a
      port (api is `expose`-only); `proactive` stays off and is absent from both `/api/branding`
      and the nav. Full loop proven through the proxy — the model called `add_planner_item`, it
      persisted to SQLite, and the Planner view rendered it. Provider was local Ollama
      (`llama3.1:8b`, keyless) reached from the container at `http://host.docker.internal:11434/v1`
      — **note `localhost` does NOT work from inside the container**; the name resolves to an IPv6
      address and worked fine.
      ⬜ Still open: the **upgrade** half (N → N+1 across two image versions, keyring survival) and
      the `minSupportedClient` gate.
- [x] Self-hoster docs (install, Tailscale, BYO key, update) — in README.md.

## Cross-cutting / anytime

- [x] Initial git commit (`0ca5bbb`, 2026-07-20)
- [x] Backend unit tests project (`PersonaOS.Tests`) — xUnit, **35 tests, all passing**,
      run with `dotnet test src/backend/PersonaOS.Tests`. Tests sit at the Application layer:
      a `TestDbContext` implements `IAppDbContext` over EF InMemory, so no Infrastructure or
      real DB is involved. Fakes in `TestSupport/` (`FakePushSender`, `FakeAiMessageStreamer`,
      `FakeTool`, …) are scriptable — extend those rather than writing new mocks.
      Covers: **ReminderDispatcher** (delivery, invalid-token pruning, retry-then-fail at 5,
      recovery, unconfigured push, no devices, feature off) — closing the Phase 4 gap;
      **ChatService tool loop** (tool executed + result fed back, parallel tool calls, unknown
      tool degrades to an error result, disabled feature hides the tool, stream failure,
      history replay, missing key); **GoalProgressCalculator**; **UserClock** (incl. the
      DST spring-forward gap). Mutation-checked: breaking `MaxAttempts` fails a test.
- [x] **Fixed: username login was case-sensitive — regression from
      the SQLite migration (`5ae9092`).** `AuthService` did a plain `u.Username == username`, so
      case sensitivity is decided by the column collation. SQL Server defaulted to
      `SQL_Latin1_General_CP1_CI_AS` (case-INsensitive); SQLite defaults to `BINARY`
      (case-SENSITIVE), and nothing configures a collation. Verified live: `purush` → 200,
      `Purush`/`PURUSH` → 401. Also means `IX_AdminUsers_Username` no longer prevents `purush`
      and `Purush` coexisting.
      **Fix (two halves, deliberately):** `AuthService.LoginAsync` now lower-cases both sides
      explicitly instead of trusting the provider's collation — that is what makes the behaviour
      testable and provider-independent, since EF InMemory (which every test here uses) ignores
      collation entirely. Separately the column is declared `.UseCollation("NOCASE")`, migration
      `20260905182358_AdminUsernameCaseInsensitive`, so `IX_AdminUsers_Username` also stops
      treating `purush` and `Purush` as different accounts.
      **Tests:** new `Auth/AuthServiceTests.cs` (10 cases: casing variants, stored-mixed-case,
      whitespace, returned casing, wrong password, unknown user, rehash path) plus
      `FakePasswordHasher` + `FakeJwtTokenGenerator` added to `TestSupport/Fakes.cs`.
      56 tests green, build 0-warning. **Mutation-checked:** restoring the plain `==` fails 7.
      **Verified live in the container** after rebuild: `purush` / `Purush` / `PURUSH` /
      `"  pUrUsH  "` all → 200, wrong password still 401, and the SQLite table rebuild preserved
      the admin row, nickname and planner data.
- [x] **Guard against weak models faking tool use (2026-09-06).** Live testing with `llama3.1:8b`
      surfaced two failures. (1) It *narrated* actions it never took — claimed a planner item and a
      reminder were created when the DB had neither; the tool loop itself was fine (an explicit
      "use the add_planner_item tool" fired and persisted correctly). (2) It printed tool calls as
      prose using the **definition** shape (`"parameters"`, not `"arguments"`); nothing executed,
      the raw JSON was shown to the user AND stored, so the next turn read it back from history and
      copied the pattern — a self-reinforcing loop.
      **Fix:** `SystemPromptBuilder` now states tool rules explicitly — never write a call as text,
      never claim an action without a tool result, never invent a tool/parameter/document name.
      New `LeakedToolCallScrubber` strips JSON objects whose `name` matches a **registered** tool
      (so ordinary JSON in a reply survives); `ChatService` scrubs before persisting, logs a
      warning, and substitutes an explanation if the whole reply was a leaked call. The `done` SSE
      event now carries `text` **only when** the stored reply differs from the streamed deltas, and
      the web client replaces the bubble on it — so the user stops seeing internals live, not just
      after a reload.
      73 tests green, 0 warnings. Mutation-checked: storing the unscrubbed text fails 2.
      ✅ Flutter updated 2026-09-10 — it now applies `text` on `done` (see the receipts entry
      below), though it is still uncompiled here for want of the SDK.
      ⚠️ Known limitation: if a user genuinely asks "show me the JSON to call get_goals", the reply
      is scrubbed. Judged acceptable — the shape needs a real tool name plus an args key.
      **qwen2.5 retest (2026-09-06), same flows, same instance:** the *action* class is fixed —
      "Whats on my list today?" → real `get_planner`; "Yes - travel to home" → real
      `add_planner_item`, item **actually persisted**; "set a reminder for 6pm" → real
      `create_reminder`, stored correctly (18:00 Asia/Calcutta → 12:30 UTC). No JSON leak; nothing
      needed scrubbing. llama3.1:8b faked all three. **Recommend qwen2.5 as the documented minimum
      for local use.**
      ⚠️ **Two hallucination classes survive the model swap:**
      (a) *Factual invention about the product* — asked how to install the mobile app, qwen2.5
      confidently said to search the App Store / Play Store for "Juno". The app is published
      nowhere. Fluent and plausible, so worse than the old JSON leak. Fix: ground the system
      prompt with what PersonaOS actually is (self-hosted; no store listing; APK from GitHub
      Releases) and tell it to say it does not know rather than invent.
      (b) *Wrong narration of a correct action* — it created the 6pm reminder correctly but
      described it as "6pm UTC, which is 12:30pm your time", inverting local and UTC. The write
      was right; the sentence would mislead. Tool receipts (showing stored values) would expose
      this; the prompt cannot reliably.
- [x] **Ground the assistant + guarantee one behavioural
      contract across providers.** qwen2.5 invented App Store install steps for "Juno"; the model
      knows nothing about the app it runs inside. Grounding goes in `SystemPromptBuilder`
      (Application), which every adapter shares — so the rules are identical for Anthropic and
      every OpenAI-compatible model by construction. **Design rule: never put behavioural or
      prompt logic in an adapter** — adapters translate wire formats only, or providers drift apart.
      **Done.** `SystemPromptBuilder` now appends (a) product grounding — self-hosted, no app-store
      listing, APK from GitHub releases, private-network access, data stays on the server, fixed
      brand vs chosen nickname; (b) the **real** project/releases URLs as constants; (c) a list of
      the modules actually enabled, naming the switched-off ones so the model stops offering them;
      (d) "say I don't know rather than guess".
      **Key lesson: forbidding invention does not work — supplying the fact does.** With only
      "never invent URLs", qwen2.5 still emitted a confident link to `github.com/PersomalAI/
      perspective`, which does not exist. Adding the true URL fixed it.
      **Cross-model proof of the contract (same prompt, same question, model swapped live):**
      qwen2.5 → correct steps with the correct URL; llama3.1:8b → *"I don't know. …refer to the
      README at https://github.com/Purush0th/personaOS."* Different fluency, neither fabricates —
      that is the achievable form of model-agnostic: identical contract, not identical prose.
      83 tests green, 0 warnings, incl. a test asserting the prompt is byte-identical across
      providers so behaviour cannot start diverging per model.
      ✅ Deduplicated 2026-09-10: the canonical repo now lives once, in
      `PersonaOS.Domain/PersonaOsProject.cs`. `SystemPromptBuilder` uses it, `/api/branding`
      serves it as `repository`, and the web `updates.service.ts` reads it from branding instead
      of its own constant — so the banner can no longer poll a different repo than the backend
      believes in. Verified live: branding returns `"repository":"Purush0th/personaOS"`.
- [x] **Tool receipts — show what the app actually did.**
      qwen2.5 stored a 6pm reminder correctly then described it as "6pm UTC, which is 12:30pm your
      time", inverting local and UTC. Prompting cannot reliably fix wrong narration of a correct
      action; showing the stored values can. Record what each tool actually executed and return,
      persist it on the assistant message, and render it under the reply so prose is never the
      only evidence.
      **Done.** `ToolReceiptBuilder` summarises each executed tool from its own result JSON —
      label (`title`/`message`/`fileName`) plus context (`dueAtLocal`/`date`/`status`), ISO stamps
      tidied, long values truncated. Receipts persist on the assistant message
      (`ChatMessage.ToolActionsJson`, migration `20260905194831_ChatMessageToolActions`), ride the
      `done` SSE event, and are returned by the history endpoint so they survive a reload. The web
      chat renders them as a quiet checklist under the reply, red when a tool failed. Built from
      tool output only — never model text — so it is provider-neutral like the rest of the contract.
      95 tests green, 0 warnings. **Verified live over the API** on the exact case that was
      misdescribed: "Remind me to call the bank at 6pm today" →
      `create_reminder ✓ "Call the bank — 2026-09-06 18:00 · pending"`. A test pins that the receipt
      shows the stored **local** time and never the UTC value, since showing UTC is the confusion
      it exists to prevent.
      ✅ **Browser rendering VERIFIED 2026-09-10**: opening an old conversation renders
      `✓ create_reminder Call the bank — 2026-09-06 18:00 · pending` beneath the reply, styled
      (border-top applied, green, bold mark) — and since it came from history, receipts survive a
      reload as intended. Superseded note below:
      ⚠️ ~~**Browser rendering is NOT yet verified**~~ — Docker Desktop crashed mid-check (orphaned
      AF_UNIX sockets under %LOCALAPPDATA%/Docker/run after a force-kill; needs a reboot). The
      Angular build compiled and deployed, but nobody has seen the receipts painted on screen.
      Confirm before treating this as complete.
      ✅ Flutter now handles both (2026-09-10): `ChatEvent` parses `actions` into a new
      `ToolReceipt`, the `done` case applies `event.text` **before** read-back (TTS would
      otherwise have spoken the raw JSON aloud) and stores the receipts, and `_ReceiptList`
      renders them under the bubble.
      ✅ **Verified: Flutter SDK 3.47.3 installed at `D:\flutter` (2026-09-10).**
      `flutter analyze` clean, `flutter test` **7 passing**. Worth noting why this mattered:
      analyze immediately found 2 real compile errors — the `actions` field had been added to
      `ChatEvent` without adding it to the constructor, so the client would not have built at
      all. Code review had not caught it. **Do not ship Dart changes on review alone.**
      New `test/chat_event_test.dart` pins the SSE parsing contract (receipts present/absent,
      failed tool, missing summary, corrected `text` on `done`) since that is precisely what broke.
- [x] **Three defects found by reading a real chat (2026-09-11).** The owner shared a goals
      conversation; the goal *was* created, but everything around it was wrong.
      1. **Receipts were blind to wrapped payloads.** `create_goal` returns `{"created": {…}}` and
         `get_goals` returns `{"goals": […]}`; `ToolReceiptBuilder` only read the root, so every
         goal receipt stored `"summary":null` — empty at exactly the moment the user needed it.
         Now unwraps single-property wrappers (bounded depth; `{"ok":true}` untouched) and adds
         `periodType`/`periodStart` to the detail fields. Verified live:
         `create_goal ✓ "Read more books — month · 2026-09-01 · active"`.
      2. **Goal periods were never normalised.** The tool documents `periodStart` as "the first
         day of the period", nothing enforced it, and a *monthly* goal got `2026-10-10`. New
         `Domain/Services/GoalPeriodCalculator` snaps month → 1st, quarter → Jan/Apr/Jul/Oct 1,
         year → Jan 1, on create **and** update. Deterministic, so it holds for any model.
         Verified live: month `2026-10-10` → `2026-10-01`; quarter `2026-09-11` → `2026-07-01`.
      3. **A dangling ```json fence leaked into the reply.** The model opened a fence mid-reply
         and never closed it; the scrubber only tidied fences after removing a JSON object, so it
         survived. Now removes an unmatched fence — the **last** one, so an earlier balanced code
         block is not torn apart (test covers that).
      116 tests green, 0 warnings.
      ⚠️ Pre-existing rows keep their old values: `Master AI` is still `2026-10-10` and the C#
      goal is still top-level. Normalisation only applies on write — no backfill was done.
- [x] **Nothing writes without the user's confirmation (2026-09-11).** The owner's call after a
      third unbidden write (a sub-goal nobody asked for, right after "Lets discuss before we add
      anything"). Prompt rules never held, so this is enforced in code instead.
      `IPersonaTool.Mutates` is declared per tool — **not guessed from the name**, so a new tool
      cannot slip past by being called something unexpected — and reads (`get_goals`,
      `get_planner`, `get_reminders`, `list_documents`, `read_document`) stay ungated, since the
      gate would be unusable if every question needed a tap. `ChatService` turns a mutating call
      into a `PendingAction` row instead of executing it, and hands the model back a result
      beginning `NOT EXECUTED`, so it stops announcing things as done. The reply renders a card
      ("Create goal “Learn Rust” — month · 2026-09-01 · **top-level**") with Confirm / Discard;
      `POST /api/chat/actions/{id}/confirm|discard` is the *only* path by which a model-requested
      write reaches the database.
      The card deliberately states parentage either way — that is the detail a model has actually
      got wrong, and "top-level" on screen catches it *before* it is saved.
      Confirm is idempotent, discard is final (confirming afterwards is refused), and proposals
      survive a reload. Migration `PendingActions`. 127 backend + 10 web specs green.
      Verified live: asking for a goal left `/api/goals` **unchanged** until Confirm; confirming
      twice created one row; a discarded proposal never ran; in the browser, Confirm turned the
      card into `✓ Water the plants — 2026-09-11 20:00 · pending` and the reminder appeared.
      ✅ **Flutter renders the cards too (2026-09-11)** — `PendingAction` model, `confirmAction`
      / `discardAction` on the API client, and a `_ProposalList` widget under the bubble with
      Confirm / Discard, replaced in place by the outcome. Double-tap guarded; a failed call
      leaves the card pending and shows a snackbar rather than losing the action. Writes work on
      mobile again. `flutter analyze` clean, **11 tests** (4 new, covering pending/confirmed/
      discarded/absent parsing).
      ⚠️ Always on; there is no "trust it" setting. Add one only if the tapping becomes tiresome.
- [x] **Flag a claimed action that has no receipt** (no-tool case, 2026-09-15). `ActionClaimDetector`
      (Application/Ai) is an English heuristic: a first-person or completed-passive change verb
      plus a domain noun (reminder, goal, planner, …), skipping questions, offers, proposals and
      negatives. When a finished reply matches and nothing was proposed this turn, `ChatService`
      runs **one corrective round**: the draft plus an "[Automatic check]" user turn naming the
      claim (never stored). The model then either calls the tool — which becomes a normal
      confirmation card — or rewrites the reply; `done.Text` replaces the streamed false claim.
      If the claim survives, or the corrective call fails, the reply is kept and stored with
      `ChatMessage.UnverifiedClaim` (migration `ChatMessageUnverifiedClaim`), sent as
      `done.unverifiedClaim` and in history. Web and mobile render an amber "This reply says a
      change was made, but nothing was saved" note; hands-free mobile speaks it too. Tests:
      `ActionClaimDetectorTests`, `ChatServiceClaimCheckTests`, mobile `chat_event_test` +
      `unverified_claim_note_test`. Live check 2026-09-15: qwen2.5 called `create_reminder` /
      `update_goal_status` properly in 5 tries, so the corrective path has not been seen live
      yet, and the web note has not been looked at in a browser. Honest replies cost nothing
      extra; a flagged one costs one more model call.
- [x] **Claim and action disagree even though a tool fired** (2026-09-23). The defence is a card
      that says exactly what will happen, in the user's terms. Tools that act on an existing item
      now implement `IPersonaTool.DescribeTargetAsync`, and `ConfirmationGate` stores the result
      on the proposal: goals ("GOAL-2 “Learn Rust”"), tasks ("TASK-7 “File taxes”"), planner items
      ("“Gym” on 2026-09-23") and reminders ("“Call mum” at 2026-09-23 19:00"). A key the target
      already names is not repeated in the details. `create_task` already says "no goal" when it
      has none, which is the parentage mismatch from the original report (goals no longer nest).
- [x] **Wider test coverage** (2026-09-23). `PlannerServiceTests` (ordering, ranges, items from
      board tasks, validation, moves, clearing), `GoalServiceTests` (keys, key parsing, status and
      progress rules, dropped goals, delete keeps tasks and planner items), `DocumentServiceTests`
      (bare names, empty files, text vs binary, delete removes bytes), `FileSystemDocumentStorage`
      (server-made names, safe extensions, refusing `../` and `..\` escapes, truncation) and
      `PersonaToolRegistryTests` (a switched-off module offers no tools, cannot be run by name,
      never needs a card; bad JSON). 481 backend tests.
- [x] Fix the wrong upstream repo slug (2026-09-01) — `personaos/personaos` was hardcoded in
      four places but the real remote is `Purush0th/personaOS`, and `release.yml` pushes images
      to `ghcr.io/${{ github.repository_owner }}/…` = `ghcr.io/purush0th/*`. So the **shipped
      compose file pulled images that will never exist**, and the update banner could never fire
      (its 404 was being read as intended fail-silent behaviour). Fixed in `updates.service.ts`,
      `README.md` clone URL, and both `deploy/docker-compose.yml` image names. Also cleared
      post-SQLite doc rot: `docs/PRD.md` (arch diagram, backend/packaging bullets, and the
      Anthropic-only prose that predates provider-neutral) and stale "SQL Server" comments in
      `AppDbContext.cs` + `GoalService.cs`. Backend build 0-warning `-warnaserror`, 46 tests green.
      ✅ Angular build confirmed 2026-09-10 — Node 24.19.0 has since been installed, `npm ci` +
      `npx ng build` run clean on the host. (The note that Node was missing is now historical.)
- [x] **Personal-data check before a public push** (2026-09-23). `git grep` for the owner's
      name, email, city, LAN address and dev password across tracked files. Fixed:
      `scripts/model-check.ps1` defaulted to the owner's dev login (now `-User`/`-Password` or
      `PERSONAOS_USER`/`PERSONAOS_PASSWORD`, and it refuses to run without them); "Chennai" and
      "call me Purush" examples in placeholders and comments became neutral ones. No `.env`,
      `google-services.json`, Firebase key or keystore is tracked. Left on purpose, for the owner
      to decide before going public: `CLAUDE.md` documents the dev credentials, and `TODO.md`
      is a working log that names the owner and their setup throughout.
- [x] **Angular 20 -> 22** (2026-09-23), one major at a time with `ng update` (core, CLI,
      Material, CDK), building and testing after each. Migrations kept behaviour:
      `ChangeDetectionStrategy.Eager` on every component (v22 changed the default) and
      `withXhr()` for HttpClient. The migration also suppressed two template diagnostics
      (`nullishCoalescingNotNullable`, `optionalChainNotNullable`); the build reports nothing with
      them on, so the suppression was left out. All pages checked in a browser on 22, a chat turn
      streamed and rendered, 26 web tests. First load grew from ~714 kB to ~762 kB.
      Also: the web app now retries branding a few times while the API starts, and shows "Try
      again" instead of a dead "could not reach" page.
- [x] **Conversation URLs use an opaque 8-char public id (2026-09-11).** `/chat/m74gjks6`.
      Owner's call: the title must not appear in the address bar (it leaks the subject into
      history, bookmarks and proxy logs) and neither should the sequential row id (it advertises
      how many conversations exist). `Conversation.PublicId` is generated by
      `Domain/Services/PublicIdGenerator` from a 31-symbol alphabet that omits the characters
      people misread when copying a link by hand (0/o, 1/l, i), using `RandomNumberGenerator`
      so ids are not guessable in sequence. Unique index; migration `ConversationPublicId`.
      `GET /api/chat/conversations/{idOrPublicId}` accepts either form, and the web client
      silently upgrades a numeric URL to the public id so the good form is what gets copied.
      ⚠️ **The generated migration was broken and had to be fixed by hand**: EF defaults every
      existing row to `""` and then creates a *unique* index, which fails outright on any
      instance with more than one conversation. Added a backfill
      (`UPDATE ... lower(hex(randomblob(4)))`) between the two. Verified on the owner's real
      database: **all 18 conversations got distinct ids**, integrity check passed. A copy of the
      pre-migration db sits in the volume at `/app/data/pre-publicid/` — delete when satisfied.
      Backfilled ids are hex (`0c311a15`); ids minted by the app use the safer alphabet.
      Verified live: click → `/chat/21e38e80`; legacy `/chat/14` resolves *and* rewrites itself
      to `/chat/0c311a15`; Back/Forward correct; unknown id → 404 → `/chat`; new thread →
      `/chat/m74gjks6`. 119 backend + 10 web specs green.
      ⚠️ The title-slug format below shipped for about an hour this same day and no longer
      resolves (`/chat/14-what-are-my-goals` → `/chat`). Never released, so nobody holds one.
      Superseded design: `/chat/<id>-<slugified title>`, e.g.
      `/chat/14-what-are-my-goals`. The **id leads** so a link survives a retitle or a title with
      nothing slugifiable (emoji/CJK) — the tail is for humans and is never used for lookup.
      New route `chat/:slug` alongside `chat`; the URL is the source of truth (the component
      loads from the route param, so Back/Forward work); opening a conversation navigates rather
      than mutating state; a new thread gets its slug via `replaceUrl` once the server auto-titles
      it. Dead id → falls back to `/chat`; slug with no id → URL normalised to `/chat`.
      Pure helper in `core/conversation-slug.ts` with 11 specs.
      **Found and fixed a real race while verifying:** `ngOnInit` awaited `refreshConversations()`
      *before* subscribing to `paramMap`. Send a message inside that window and the subscription
      fired late with no slug, resetting `conversationId` to null and **wiping the thread on
      screen mid-reply**. Now it subscribes first, and the null branch refuses to clear while
      `streaming()`. Reproduced (submit immediately after load) and confirmed fixed.
      Also repaired `app.spec.ts`, which was still the untouched CLI scaffold: it provided no
      `HttpClient` (BrandingService needs it) and asserted the long-deleted "Hello, PersonaOS.Web"
      text, so `ng test` had been red. **12 web specs green**, `ng build` clean.
      ✅ Delete shipped 2026-09-11 (see below), so throwaway threads can be cleared.
- [x] **Conversations can be deleted (2026-09-11).** `DELETE /api/chat/conversations/{idOrPublicId}`
      plus a ✕ on each sidebar row. Messages, receipts and any **unconfirmed proposals** cascade,
      so nothing is orphaned — a pending write must not outlive the thread that proposed it.
      Irreversible, so the row turns into an explicit "Delete this conversation? Delete / Cancel"
      rather than acting on one stray click; the ✕ stays hidden until hover/focus-within so it
      cannot be hit by accident. Deleting the thread that is **open** clears it and returns to
      `/chat`, since the URL would otherwise point at something gone.
      130 backend + 10 web specs green. Verified in the UI: Cancel deletes nothing (22 rows before
      and after), Delete removes exactly one row, and deleting the open thread redirected
      `/chat/00b13469` → `/chat` with the transcript cleared.
      ⚠️ Flutter has no conversation list at all, so nothing to add there yet.
- [x] **Web app is usable on a phone (2026-09-10).** `.topbar` was a no-wrap flex row with six
      nav links, brand and Sign out; on a narrow screen Reminders/Documents/Settings were simply
      clipped and the page scrolled sideways. Now the topbar wraps, and under 720px the nav takes
      its own full-width row that scrolls horizontally. Verified at a real 380px viewport:
      media query matches, `document.scrollWidth` no longer exceeds the viewport, and scrolling
      the nav reaches Settings. Update banner wraps too; content padding tightened on mobile.
- [x] **Web nav: chat history under Chat, and a light/dark switch (2026-09-23).** The conversation
      list moved out of the chat page into the nav, as an expandable group under Chat (state
      remembered per browser). Chat is the last nav item and its history takes the remaining
      height with its own scroll, so the pages above and the footer below never move. Settings
      and Sign out moved into an account menu in the footer (`mat-menu`, opens upward), labelled
      with the username read from the JWT's `unique_name` claim.
      `ConversationsStore` (core) is the one copy shared by nav and chat page; delete lives there
      and lazy-loads the confirm dialog so the dialog code stays out of the first load. The nav
      footer has a light/dark button (`ThemeService`): the device decides until the user picks,
      the pick is `data-theme` on `<html>` and is applied by an inline script in `index.html`
      before first paint. Also fixed `app.spec.ts`, failing since the Material commit (the brand
      only renders once signed in), and added a spec for the history list. 11/11 web tests.
- [x] **Web: finish the Material migration and fix field sizing (2026-09-23).** The owner
      spotted uneven fields on the backlog. Root cause: `_legacy-primitives.scss` was still
      imported globally, and its bare `label { display:block; margin:.9rem 0 }` rule hit
      Material's floating label (which is a `<label>`), pushing labels down inside every field.
      Deleted the file; migrated the setup wizard (now behind `@defer`). Four component
      stylesheets had kept pre-Material rules (undefined `--muted`/`--line`, hardcoded light
      greys, bare `input`/`textarea` rules): task, goal and sprint pages now share
      `board/item-page.scss`, and discussion and push-config were rewritten on `--mat-sys-*`
      tokens. One field size app-wide: 40px (button height) with 14px text, via
      `mat.form-field-overrides` in `styles.scss`; outline and dynamic hint spacing are the
      defaults in `app.config.ts`, so no template sets them. Backlog rows show priority and points
      as compact chips that open shared menus instead of full-size selects. Cards whose forms sat
      against the border got `mat-card-content`. Card subtitles use body text, not a second bold
      title. Audit command for leftovers:
      `grep -rnE "var\(--(muted|line|ink)|#[0-9a-f]{3,6}\b" --include=*.scss src/app` (empty).
- [x] **Chat renders Markdown** (2026-09-23). Replies showed `**bold**`, backticks and `-`
      lists literally. `shared/markdown.pipe.ts` renders them with `marked` (GFM, single line
      breaks kept), escapes any raw HTML the model writes instead of passing it through, opens
      links in a new tab with `noopener`, and still goes through Angular's sanitizer via
      `[innerHTML]`. Loaded with the chat route only, so the first download is unchanged. The
      user's own messages stay plain text. Spec covers rendering, escaping and link attributes.
- [x] **Chat page no longer scrolls twice on a phone** (2026-09-23). `chat.scss` sized the page
      `calc(100vh - 6rem)`, ignoring the 56px handset toolbar. The shell's content area is now a
      flex column on the chat route (`.mat-drawer-content:has(app-chat)` in `styles.scss`), and
      the page takes the height left over, so only the message list scrolls. Measured at 375x812
      and 1280x720: page scroll height equals its client height.
- [x] **Web first-load budget** (2026-09-23). Measured with `ng build --stats-json`: the form-field
      defaults imported in `app.config.ts` pulled the form-field module into the shell. They moved
      to `core/form-field-defaults.ts`, provided by a lazy parent route (`pages.routes.ts`, which
      now holds every page) and by the setup wizard: 762 kB -> 699 kB raw, 175 -> 166 kB
      compressed. What remains is the floor for this shell: Angular core and router, the CDK
      overlay (the account menu), and Material's sidenav, list, button and menu. `@angular/forms`
      (50 kB) stays because `MatListModule` includes `MatSelectionList`, and importing the list's
      parts one by one does not shake it out; dropping it would mean hand-building the nav instead
      of using Material's, for about 20 kB compressed. The budget is now 750 kB warning / 900 kB
      error, set deliberately against that floor.
- [x] **Board behaves like the owner's Jira board** (2026-09-24). Observed on the owner's Jira
      board (via Claude in Chrome) and matched on web: cards drag from anywhere (the grip is gone;
      `core/drag-defaults.ts` adds a 250 ms press on touch so swipes still scroll). Columns are
      separate CDK drop lists, so the dragged card stays faded in its own column while the column
      under the pointer lights up with a "This week -> In progress" label (`onDragMoved`); a drop
      there moves the card to the end of that column, a drop in its own column reorders it. A click
      opens the task in a dialog over the board or backlog, driven by `?task=TASK-3`
      (`board/task-dialog.ts`: Esc/close drops the parameter, Back closes the dialog, and it is
      created from the page's injector so it keeps the route's outlined form fields). Every column
      has an inline Create ("What needs to be done?", Enter creates and stays open); the API's
      `CreateTaskRequest` gained an optional `Column` (in progress/done only in a running sprint).
      The card menu shows on hover. `board.spec.ts` drives real CDK drags; synthetic mouse events
      need `detail: 1` (CDK ignores screen-reader clicks) and `view: window` (or pageY skips scroll).
- [x] **Board and backlog look like Jira, not like a form** (web, 2026-09-24). Measured on the owner's
      Jira (14px text, 280px lanes 12px apart on a surface darker than the page, raised cards with a
      shadow and no outline, 40px list rows, grey/blue/green status lozenges, compact buttons) and
      matched: `--work-lane/--work-item/--work-item-shadow` and `--status-*` tokens per scheme in
      `styles.scss`, `.lozenge` for statuses and per-status point totals, a quiet `.create-trigger`,
      and 32px square-cornered buttons scoped to `app-board, app-backlog`. The board's sprint card is
      now one line; the backlog shows a status lozenge (a menu that moves the task, only To do before
      a sprint starts), priority as its icon, and Create sprint in the backlog section. Board and
      backlog share `shared/inline-create.ts` (Enter creates and stays open; the title is read from
      the field because ngModel cannot see '' -> '' after a create). The task dialog drops the card
      outlines in its main column (`TaskDetail` gets `.embedded`) and uses 12px corners. Specs:
      `inline-create.spec.ts`, `backlog.spec.ts`. Checked by headless-Chrome screenshots at 1440 and
      390px in both schemes, since the Browser pane and Chrome tab did not paint in the background.
- [x] **Board gets a Reports tab** (web, 2026-09-24; owner, pointing at the "Sprint view" button:
      "Reports"). The board's views are now tabs as in Jira, Sprint | Backlog | Reports, in one shared
      frame (`board/board-tabs.ts`: title, tabs, a `boardActions` slot, and the page inside the
      mat-tab-nav-panel Material requires). `/board/reports` (`board/reports.ts`) uses the report API
      the web app never called: a velocity chart of commitment (grey) against completed (green) per
      started sprint, oldest first, each bar linking to that sprint's burndown page; the average
      velocity (last three finished sprints) or a note that none has finished; and a table of every
      started sprint. The board's "Sprint view" button is gone; the sprint page's crumbs link to
      Reports. `.empty-state` moved to `styles.scss` (board and reports share it). Spec:
      `reports.spec.ts`, plus a check that an open task (`?task=`) keeps the Sprint tab lit.
- [x] **Reports: one sprint at a time, with its tasks** (web, 2026-09-24; owner: "report page should
      show list of work items of that sprint ... search old sprint", after Jira's Reports page).
      `/board/reports?sprint=SPRINT-3`: a "Find a sprint" search (autocomplete over every started
      sprint, `report(104)`), then for the chosen one (default: running, else latest) four tiles
      (tasks done, points completed of committed, points added/removed, days left or finished),
      "Tasks by status" and "Tasks by priority" as stacked bars (`board/breakdown-bar.ts`, the plain
      stand-in for Jira's doughnuts), and "Task details" (`board/work-item-table.ts`: search over
      key/title/goal, headings that sort, rows opening the task dialog). Velocity stays at the
      bottom; its bars now pick the sprint. The sprint table from the first cut is gone. Loading
      uses plain signals and an effect, not `resource()`: resource's code sits in @angular/core's
      shared chunk, and using it here moved 6 kB into every page's first download. Specs:
      `reports.spec.ts`, `work-item-table.spec.ts`, `breakdown-bar.spec.ts` (web: 64 tests).
- [x] **Burndown inside Reports** (web, 2026-09-25; owner: include it above the task details and drop
      the separate Burndown link). The chart moved out of the sprint page into
      `board/burndown-chart.ts`, used by both. It now stretches to its container at a fixed height:
      only the line is SVG (percent coordinates, `vector-effect: non-scaling-stroke`), and the dots
      and labels are HTML at the same percentages, so text stays at reading size at 390px and at
      1440px (the old scaled SVG blew its labels up on a wide screen and shrank them on a phone).
      With fewer than two days of data it says the line starts after a day. Spec:
      `burndown-chart.spec.ts`, plus a Reports check that it sits right above Task details.
- [x] **The chart is called Burndown** (web, 2026-09-25; owner: "Points remaining" means nothing for a
      finished sprint). Heading "Burndown" on Reports and the sprint page; the chart carries the
      caption "Points left at the end of each day", which reads right for a running sprint and a
      finished one, and its screen-reader label starts "Burndown:".
- [x] **Sprint dates: pick a start day, the end follows; no starting a sprint before its day**
      (2026-09-25; owner started SPRINT-2 on Friday although it starts Sunday, and could not edit the
      dates in the form). Server: `CreateSprintRequest`/`UpdateSprintRequest` take `StartsOn` (a day;
      the rhythm's 20:00 is added), and a new start moves the end to `SprintCalendar.CloseAfterStart`
      unless an end is given (the assistant's tools still can). `StartSprintAsync` refuses before
      the sprint's first local day ("SPRINT-2 starts on Sun, Sep 27. Move its start to today to begin
      it now."); on that day any hour works, so the Sunday 18:00-20:00 planning window can start it.
      Web: one `board/sprint-form.ts` for the backlog (new, edit) and the sprint page (edit) - name,
      a native date field (opens the calendar on click; Material hides the browser's icon, so the
      form has its own), and the end shown read-only as it changes. Material's datepicker was tried
      and dropped: it put 5.5 kB into every page's first download. Start sprint is greyed out with a
      reason until the day comes (`sprintDayHasCome`). Specs: 4 backend (500 total), web
      `sprint-form.spec.ts`, `core/sprint-dates.spec.ts`, a backlog Start check (79 total).
      Live note: SPRINT-2 was already started early and keeps its dates; moving its start to Sep 25
      now would also move its end to Sun Sep 27, per the rule.
- [x] **Statuses read Backlog, To do, In progress, Done** (2026-09-25; owner: "This week" should be
      "To do" once a task is in a sprint). The `todo` column is labelled "To do" everywhere: web
      `COLUMN_LABELS` (goals.ts had its own copy of the labels; it uses the shared one now), mobile
      `BoardColumns.label`, the assistant's board prompt and get_board/move_task descriptions, the
      morning brief ("From the sprint:"), and docs/sprint-board.md. Backlog rows show a plain grey
      "Backlog" lozenge (a sprint row's lozenge stays the menu that moves the task). The API values
      (`backlog`, `todo`, `in_progress`, `done`) are unchanged. The phone shows the new label from the
      next APK.
- [x] **Shorter wording** (web + mobile, 2026-09-25; owner: "Edit dates and name" -> "Edit", "Delete
      sprint" -> "Delete", and the same wherever text over-explains). Menus: Edit, Delete, Open (was
      "Open as a full page"). Unestimated points and an unset sprint end read "–" (was "?" and "The
      Sunday after it starts"). Shorter hints and empty states on the board, backlog, Reports,
      burndown, sprint form, task page, attachments, goals, documents, reminders and four Settings
      notes; on the phone the WIP note, the split hint, the velocity line, the delete confirmations
      and the planner tooltip. Confirmations for deletes that cannot be undone keep their warning.
      House style from here: a label names the action in one word where one word is clear.
- [x] **"What are my goals" answered in 200 words of made-up detail** (backend, 2026-09-25; live
      qwen2.5:3b-instruct). The reply invented a description for every goal (all three are empty),
      read out "no comments or attachments", and told the user to "call the create_goal,
      update_goal_status, or delete_goal functions". Three fixes, none per-reply: (1) every tool
      writes its result through one `Ai/Tools/ToolJson` (five tool families each had their own
      copy of the options), which leaves out null fields, empty lists inside an item, zero
      comment/attachment counts and `sortOrder` (a tool's own top-level list still shows when
      empty); (2) the prompt asks for short replies, one line per item, never to name a tool, and
      never to describe a field the tool did not return (identity + tool-rules, full and compact);
      (3) a `ToolNameGuard` in the reply pipeline drops any sentence naming one of the turn's
      tools. The sentence helper it shares with `CardInstructionGuard` now keeps paragraph breaks
      when it drops a sentence (it used to run the paragraphs together). Checked on the same model
      locally: three runs, one line per goal, no tool names, nothing invented; it still sometimes
      closes with a question. Tests: `ToolJsonTests`, three `ToolNameGuard` tests (507 total).
- [x] **Same causes, other places** (2026-09-25; owner: when one bug is reported, check where else it
      applies). Tool names also reached the user through the chat UI: "Using get_goals…" while a tool
      ran and `get_goals` in the list of what was done, on web and phone. `Ai/ToolLabel` now words a
      tool three ways (card "Create goal", running "Reading goals…", done "Created goal"); the stream
      sends `toolLabel` and every `ToolReceipt` carries `label` (worked out from the name, so receipts
      stored before this have it too); web and phone show those. The confirmation card's wording
      moved into the same class. The other read tools were asked their question on the same model:
      "what is in my backlog" called get_planner, so get_plan now says it is the tool for the backlog
      and get_planner says it is not; get_board and get_sprint_report said a velocity of 10 with no
      finished sprint, so both now say velocity is missing until one finishes. Still open, and not a
      prompt problem: qwen2.5:3b misreads numbers it is given ("10 of 10 points done" with 0 done)
      and mixes sprint tasks into "the backlog". Re-test on a 7B model once the GPU is in.
      Tests: `ToolLabelTests` (515 backend total), phone `ToolReceipt` label tests (85).
- [x] **Goals page, and hand-set progress everywhere** (web + phone, 2026-09-25; owner: the goals page
      is clumsy and the progress box is disliked). The page reads like the backlog: each goal is a
      lane with key, title, period and status lozenges and dates, its actions (Complete, Drop/Reopen,
      Delete) in a menu instead of four buttons, its tasks as the backlog's raised rows (now the
      shared `.work-row` in styles.scss, which the backlog uses too) opening in the task dialog, and
      "Create task" as the shared inline create. Progress is one `goals/goal-progress.ts`: for a goal
      kept by hand the bar itself is a slider in 5% steps that saves when let go; with tasks it only
      reads out ("0 of 8 points · 0 of 2 tasks"). Same cause elsewhere: the goal's own page had the
      same number field (and a separate bar and line), and the phone's "Set progress…" was a text
      field in a dialog; both use a slider now, and the goal page's task list and create match the
      goals page. Specs: `goal-progress.spec.ts` (web 82), phone `goal_progress_test.dart` (87).
- [x] **The key is the link, not the title** (web, 2026-09-26; owner: task text should not be the
      hyperlink, the task id should, everywhere). One style, `a.key-link` in styles.scss (primary
      colour, underline on hover). Tasks: backlog rows, goals page and goal page rows, sprint page
      lists, the Reports task table, and board cards, where the key is now the stretched link (a
      click anywhere on the card still opens it; the card still shows the drag cursor away from
      the key). Same pattern elsewhere: goal titles on the goals page and sprint names in the
      backlog and the board's sprint line had been the links; their keys (GOAL-2, SPRINT-1) are
      now. The planner already linked the key only. The phone has no links (rows are tapped), so
      nothing changed there. Specs: board, backlog and task-table checks (web 85).
- [x] **Update progress from the goal's menu** (web + phone, 2026-09-26; owner: the menu should
      offer changing progress, in a popup). Goals page: the ⋯ menu of a goal without tasks has
      Update progress, which opens `GoalProgressDialog` (goals/goal-progress-dialog.ts: slider in
      5% steps, the number above it, Save off until the value moves). The bar on the page only reads
      out now (`GoalProgress` lost its slider and `changed` output). Goal page: an Update progress
      button beside the bar opens the same dialog. Goals with tasks still take progress from them,
      so neither offers it. Phone: the menu item and dialog title say Update progress too (was
      "Set progress…" / "Progress"). A spec caught Cancel closing with '' rather than undefined,
      which would have saved an empty value; askGoalProgress only accepts a number. Web 86 tests.
- [x] **Update progress dialog scrolled** (web, 2026-09-26; owner saw a scrollbar in it). The
      slider's 18px handle sat on a 6px input and stuck out 6px, so `mat-dialog-content` (92px of
      content in 86px) scrolled. The input is now as tall as the handle and draws the 6px bar
      across its middle. The spec checks the content does not scroll (fails on the old style).
      Missed at first because the screenshot script hid scrollbars; it no longer does.
- [x] **A goal's page is /goals/GOAL-1, not /board/goals/GOAL-1** (web, 2026-09-26; owner: the
      board holds sprints, tasks and reports; goals stand alone). Route `goals/:key`, the page
      moved to goals/goal-detail.*, and `board/goals/:key` redirects so old links still work. Links
      from the goals list and a task's goal chip use the new address, and the page's crumb is just
      Goals. The sidebar now highlights Goals on a goal's page (it lit Board). The phone and the
      backend had no page links to change. Spec: pages.routes.spec.ts (web 88).
- [x] **PRD brought up to date** (docs, 2026-09-26; owner asked for the latest PRD to review). It
      last changed on 2026-09-01 and still described nested goals, no board, Anthropic plus one
      adapter and phases only. Now: product principles (confirmation cards, receipts, one contract
      for every provider, keys, short wording, manual plan), each module's rules, the chat guards
      and tools, architecture with the Ollama adapter, model profiles and prompt files, clients
      and their addresses, data and the release gate, the board and AI phases, what is deferred,
      and open product questions. docs/sprint-board.md aligned too (Reports tab, statuses, "–",
      sprint start day and end, Burndown, key links); its out-of-scope list no longer excludes
      charts that now exist.
- [x] **Goal types read Monthly, Quarterly, Yearly; the chat's waiting signs move** (web + phone +
      backend, 2026-09-27; owner's corrections). Types: `PERIOD_LABELS` / `periodLabels` are
      Monthly, Quarterly, Yearly in that order (the pickers offer them so, Monthly first), and one
      helper words goals of a type ("Add quarterly goal", "1 of 3 monthly goals done", "Delete its
      quarterly goals first.", the goal page's "Quarterly goals" list and Type line). The assistant's
      cards and receipts show the type the same way (`GoalPeriods.Label`), not the raw "month". The
      slot pickers keep "Quarter" / "Month" (they pick Q1-Q4 or a month). Chat: the reply bubble
      showed a still "…" on web and phone, and the web's send button a still hourglass; now three
      dots brighten and rise in turn (web CSS, fading only under reduced motion; phone `TypingDots`,
      still when the phone turns animations off) and the send button spins while a reply streams.
      Checked mid-reply in headless Chrome (dot opacity changing between samples, spinner present),
      phone `typing_dots_test.dart`. Tests: backend 521, web 99, phone 88.

