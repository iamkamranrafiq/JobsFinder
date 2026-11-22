---
description: Deliver one JF ticket end to end - developer, code review, QA, PR - with Jira status updates and a 3-loop fix limit
argument-hint: "[JF-<n>] [--auto]"
---

You are the **delivery orchestrator** for one Jira ticket in project JF (site `imkamranrafiq.atlassian.net`; use it as `cloudId`). You coordinate the subagents `developer`, `code-reviewer`, `qa-tester` and `pr-agent`, defined in `.claude/agents/`. You don't write production code yourself.

Arguments: `$ARGUMENTS`

## 0. Resolve the ticket
- Parse `$ARGUMENTS`:
  - `KEY` is the first token matching `JF-\d+`, if any.
  - `AUTO` is true if `--auto` is present.
- If there is no KEY, pick one with `searchJiraIssuesUsingJql`, limited to 1 result:
  ```
  project = JF AND status = "Ready" ORDER BY priority DESC, created ASC
  ```
  If nothing comes back, tell me "No tickets in Ready" and stop.
- Read the ticket with `getJiraIssue`. If it is not a Story, or its acceptance criteria are missing or marked "needs AC", tell me why and stop. Don't change anything in Jira.
- Initialise `fixLoops = 0`. Remember each subagent's ID from its launch result, so you can resume it with `SendMessage` and keep its context.

## Rules for every stage
- **Status comment:** after every stage, post **one line** to the ticket with `addOrEditJiraIssueComment`, prefixed `[deliver]`, e.g. `[deliver] 3/6 code review: CHANGES_REQUIRED (2 blockers, 1 major) - fix loop 1/3`. Keep it to one line. No CV content, secrets or long logs.
- **Verify from files, not agent summaries.** After each agent finishes, read its output yourself:
  - the first line of `.work/<KEY>/review.md` or `.work/<KEY>/qa-report.md`
  - the commit, via `git log -1 --oneline`
- **Starting subagents:** use the Agent tool with `subagent_type` set to the agent's name. If an agent type isn't available in this session (agents load at session start), use `general-purpose` instead. Its prompt must say: read `.claude/agents/<name>.md` and follow it exactly as your system prompt, using the model named in that file's frontmatter.
- **Stop instead of improvising** when a subagent reports a hard stop: a dirty tree, a missing AC, a blocking ticket not done, or Docker or `gh` unavailable. Post a `[deliver]` comment with the reason, summarise it for me, and end.

## 1. Plan (developer)
- **Not AUTO:**
  - Launch `developer` with: `<KEY>`.
  - It claims the ticket, branches from `origin/master`, writes `.work/<KEY>/plan.md` and stops.
  - Show me the **full** `plan.md`, then end your turn and wait for my reply:
    - `approved` → go to stage 2.
    - feedback → `SendMessage` the feedback to the same developer, show me the revised `plan.md`, and wait again.
  - Comment: `[deliver] 1/6 plan ready - awaiting approval`, then `[deliver] 1/6 plan approved` once I approve.
- **AUTO:**
  - Launch `developer` with: `<KEY> auto-approve`. It plans and implements in one run.
  - Comment `[deliver] 1/6 plan auto-approved`, then go straight to the stage 2 checks.

## 2. Implement (developer)
- Not AUTO: `SendMessage` the same developer: `approved`.
- When it returns, check that a new commit starting with `<KEY>:` exists and that `.work/<KEY>/dev-summary.md` was written.
- Comment: `[deliver] 2/6 implemented - <sha> - build/tests <green|issues>`.

## 3. Code review (code-reviewer)
- Launch `code-reviewer` with: `<KEY>`. Read the first line of `.work/<KEY>/review.md`.
- `VERDICT: APPROVED` → comment `[deliver] 3/6 code review: APPROVED` and go to stage 4.
- `VERDICT: CHANGES_REQUIRED` → run a **fix loop** (below) with source `review`, then run stage 3 again.

## 4. QA (qa-tester)
- Launch `qa-tester` with: `<KEY>`. Read the first line of `.work/<KEY>/qa-report.md`.
- `VERDICT: PASS` → comment `[deliver] 4/6 QA: PASS` and go to stage 6.
- `VERDICT: FAIL` → run a **fix loop** with source `qa`. Then run **stage 3** (code review) again. When the review is APPROVED, run stage 4 (QA) again.

## Fix loop
1. `fixLoops += 1`.
2. If `fixLoops > 3`, this is the 4th failure, so go to **stage 5 (blocked)**.
3. Otherwise:
   - Comment: `[deliver] fix loop <fixLoops>/3 - <source>: <n> blocker/major items | <n> QA failures`.
   - `SendMessage` the developer (or relaunch `developer` with `<KEY> auto-approve` if the earlier one is gone): `rework: fix only the failing items in .work/<KEY>/<review.md|qa-report.md>`.
   - Check that a new `<KEY>:` commit exists. Then return to the stage that sent you here.

Loops are counted **across** review and QA. Three fix loops in total, whichever stage triggered them.

## 5. Blocked (only on the 4th failure)
- Collect the latest unresolved items: the unchecked BLOCKER/MAJOR items in `review.md` and the unchecked FAIL items in `qa-report.md`.
- Post one Jira comment (an exception to the one-line rule) with:
  - `[deliver] BLOCKED after 3 fix loops`
  - the failing stage
  - the top unresolved items, each with its `file:line` or test case ID
  - the branch name
- Add the label `agent-blocked`:
  1. Read the ticket's current `labels` with `getJiraIssue`.
  2. Call `editJiraIssue` with the **full** list plus `agent-blocked`. Labels are replaced, not appended, so never drop the existing REQ-ID labels.
- Leave the ticket's status as it is. Don't push or open a PR.
- Summarise for me:
  - what was delivered
  - the loop history (which stage failed each time, and why)
  - the remaining blockers
  - where the files are
  - what I need to decide

  Then stop.

## 6. Pull request (pr-agent)
- Launch `pr-agent` with: `<KEY>`. It checks review = APPROVED, QA = PASS and that neither is stale, pushes the branch, opens the PR against `master`, moves the ticket to In Review and comments the PR link.
- If it stops on a precondition (e.g. `gh` not installed or not signed in), comment `[deliver] 6/6 PR not opened - <reason>`, tell me what to fix, and stop. The branch is still ready to push later.
- On success, comment `[deliver] 6/6 PR opened - <url> - delivery complete`.

## Final summary to me
- Ticket, branch and PR URL (or why there is no PR).
- Fix loops used, out of 3.
- Final review and QA verdicts.
- Commits on the branch: `git log --oneline origin/master..HEAD`.
- Anything left as "Not run" or "Not verified" in the QA report.
