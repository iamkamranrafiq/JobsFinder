---
description: Refine a raw requirements document - ask clarifying questions first, then write the refined spec
argument-hint: <path to requirements file (.pdf, .md, .docx, .txt)>
---

Act as a senior business analyst and solution architect. Refine the requirements document at: `$ARGUMENTS`

This is a two-phase process. **Phase 1 only asks questions. Do not write or edit any file until the user has answered and confirmed what to produce.**

## Phase 1 - Read and question

1. **Read the whole document.**
   - If `$ARGUMENTS` is empty or the file does not exist, say so and stop. If the path is close to an existing file (for example `requirements/` vs `requirement/`), say which file you found and use it.
   - For a PDF, try the Read tool first. If page rendering is unavailable, extract the text with `pdftotext -layout "<file>" <scratchpad>/<name>.txt` and read that. Tables in extracted PDFs are often misaligned: say so, and ask about any table you cannot reconstruct with confidence. Never guess its contents.
   - Also read `CLAUDE.md` and look at the current codebase. Contradictions between the document and what already exists (for example "Phase 0 is done" when no code exists, or a different framework) are conflicts to raise.

2. **Write numbered questions** (one sequence across all groups, so the user can reply `12: ...`). Group them as:
   - **A. Conflicting requirements:** two parts of the document, or the document and the codebase, that cannot both be true.
   - **B. Ambiguities and missing definitions:** terms, thresholds, defaults or behaviours the document leaves open. Push for the definitions that invariants depend on (for example what exactly counts as "submitted").
   - **C. Missing edge cases:** concurrency, stale state, limits reached mid-operation, deletion vs audit, partial failures, unusual inputs, users whose situation the happy path ignores.
   - **D. Missing NFRs**, each with concrete numbers requested:
     - performance and scale (load, p95 latencies, concurrency)
     - availability and recovery (uptime, RPO/RTO)
     - security (MFA, token lifetimes, operator access, malware, log redaction)
     - audit (what is logged, who can read it, retention, tamper-evidence)
     - privacy and compliance (jurisdictions, data residency, processor agreements, consent)
     - rate limits and abuse
     - cost targets
     - billing
   - **E. Delivery:** dates, team size, how phases map to tickets.

   Rules for each question:
   - One short paragraph. Cite the section it comes from and why it matters.
   - Where a sensible default exists, add *(Default: ...)* so the user can reply "default".
   - Don't ask about anything the document already answers clearly.

3. **Finish Phase 1 with a short list of the top ~10 blocking questions**: the ones whose answers change the design most. Say that you will use the stated defaults for the rest unless told otherwise, and that the user can answer in the form `3: ...`. Then stop and wait.

## Phase 1b - Follow-ups

When the user answers:
- Restate each decision in one line so they can spot misreadings.
- Ask a numbered follow-up (`F1`, `F2`, ...) only where an answer:
  - is ambiguous,
  - contradicts another part of the document or another answer (for example a free tier that ends up larger than a paid one), or
  - removes something other parts depend on (for example dropping a status that a success metric is computed from). Name the knock-on effects.
- If a decision reverses a guardrail or a "non-negotiable" in the document, or carries legal, terms-of-service, security or cost risk:
  - State the consequences once, plainly and specifically.
  - If it is the user's call to make, record it as an **accepted risk** and carry on. Do not re-argue it.
- Repeat until there are no open follow-ups. Then ask which output to produce:
  1. the refined requirements document,
  2. a backlog (for example Jira epics and stories, if a tracker is connected),
  3. both.

  Wait for the answer.

## Phase 2 - Write the refined document

Write `docs/requirement/refined/<source-file-name-in-kebab-case>.md`. If a file with that name already exists, ask before overwriting it.

Structure:
1. **Header:** title, source file path, date, status (`Refined - vN`), owner.
2. **Decision log:** a table with one row per answered question or follow-up: `#`, `Question (short)`, `Decision`, `Impact on the spec`. Mark defaults that were applied without an explicit answer as `Default`.
3. **Accepted risks:** each with the risk, its consequence, who accepted it, and any mitigation still in place.
4. **Refined specification:** the original document rewritten so it is internally consistent with every decision.
   - Keep the original section order and the useful content.
   - Remove dropped features entirely, including the metrics, analytics, entities, endpoints and roadmap items that depended on them. Don't leave them half-present.
   - Reword guardrails that a decision changed. Never leave two statements that contradict each other.
   - Rebuild any table that was garbled in the source.
   - Make every threshold, limit and NFR a concrete number.
5. **Edge cases:** each one with its expected behaviour.
6. **Non-functional requirements:** a table of category, requirement, and target or measure.
7. **Pending / deferred:** items the user explicitly postponed, with the phase they were deferred to.
8. **Open questions:** anything still unresolved. If there is nothing, write "None".

After writing it:
- Re-read the file and check that no removed or reversed requirement survives elsewhere in it.
- Give the user the file path and a 3-5 line summary of what changed from the source.
- Do not commit it unless asked.
