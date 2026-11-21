# Job-Finder - Product & Engineering Documentation (Refined)

| | |
|---|---|
| **Source** | `docs/requirement/raw/Job-Finder — Product & Engineering Documentation.pdf` (Nov 7, 2026, v1.0 scope) |
| **Refined on** | 2026-11-22 |
| **Status** | Refined - v2 (open questions resolved 2026-11-22) |
| **Owner** | Kamran |

The raw PDF's tables were misaligned when converted to text (Tech Stack, Core Modules, API Surface, Pricing and Roadmap). They have been rebuilt below and their contents confirmed during refinement.

---

## 1. Decision log

**Source** shows where each decision came from:
- **Answered:** the owner answered the question directly.
- **Default:** the owner was offered this default and did not object.
- **Derived:** this document fills in a value that follows from another decision. Values marked Derived are listed under Open questions for confirmation. (None remain: all were confirmed in OQ4.)

| # | Question (short) | Decision | Source | Impact on the spec |
|---|---|---|---|---|
| Q1 | Does MVP code exist? | No. Everything is built from scratch. | Answered | Phase 0 changes from "Done" to "Planned". The "MVP already runs" statements are removed. |
| Q2 / F2 | What counts as "Submitted"? | **Web form:** the site displays its confirmation page or message. **Email:** the mail server (SMTP / Gmail / Graph) accepts the message. | Answered | Defines the honesty invariant and the quota unit (section 3.21). |
| Q3 | What is a tenant? | One person. Tenant and user are 1:1 in v1. | Answered | The coach/team view and "many users per tenant" are removed. |
| Q4 / F3 | Plan limits | Daily limits: Free 1, Basic 5, Standard 15, Premium fair-use unlimited. | Answered | Monthly quotas are replaced by daily limits. The counter resets daily. |
| Q5 / F4 | Tracking after submission | Removed: viewed / interview / offer / rejected, employer responses, the interview-rate metric, and analytics on which tailoring wins interviews. | Answered | The tracker shows only agent-side states. Mailbox access stays send-only. |
| Q6 | Linking LinkedIn | Removed. | Answered | Screening answers come from the CV and stored preferences only. |
| Q7 / F1 | Sourcing and logins | No signing in to any third-party site. Jobs are sourced through **Apify scrapers for all sources, including LinkedIn and Indeed**. | Answered | Sourcing is rewritten. The guardrail is reworded. Recorded as an accepted risk (section 2). Stored site logins are removed. |
| Q8 | Current employer | Block automatically, including known parent and subsidiary companies. | Answered | New profile field and blocklist rule. |
| Q9 | Diversity (EEO) questions | Ask the user once and store the answers (sensitive data). | Answered | New `EeoAnswers` entity: encrypted, consent-gated, editable and deletable. |
| Q10 | Payments and jurisdictions | Deferred to the last phase. | Answered | Listed under Pending (section 6). |
| A1 | Apify budget and frequency | Use the Apify free tier. Scrape each source once per day. | Answered | Sourcing is shared across all tenants and capped by the free-tier credit (section 3.11). |
| A2 | Open-source licence | Decide later. | Answered | Listed under Pending. |
| A3 | Delivery | Built solo by the owner. Dates are not yet set. | Answered | Listed under Pending. Roadmap phases have no dates. |
| D1 | Model routing | Fit scoring uses Sonnet-class models. Tailoring and difficult screening answers use Opus-class models. | Default | Resolves the conflict between the Tech Stack and the routing table. |
| D2 | BYOK provider | Anthropic only in v1. | Default | Other providers still go behind `IAIProvider`. |
| D3 | Endpoint style | Minimal APIs. | Default | Replaces "controllers" in the layering text. |
| D4 | UI library | Angular Material. | Default | NG-ZORRO is dropped. |
| D5 | Database | PostgreSQL everywhere, including local development (Docker). | Default | SQLite is dropped. |
| D6 | Background work | A durable Postgres-backed job queue from Phase 0. | Default | Replaces "in-process Channels for MVP", so cycles can resume after a restart. |
| D7 | Fit threshold, scoring cap, freshness, schedule | Threshold 70. At most 50 roles scored per tenant per day. Postings older than 30 days are dropped. One cycle per saved search per day. | Default | Concrete values in sections 3.11, 3.14 and 5. |
| D8 | Duplicate detection | Content hash **plus** same company + normalised title within 30 days. | Default | Catches reposts and the same job on several sources. |
| D9 | CVs per user | One active CV per user in v1. Each application stores the profile version it used. | Default | `CandidateProfile` is versioned. |
| D10 | Queue item expiry | Items expire after 7 days. Approving one whose posting has closed or whose profile has changed requires re-preparing it. | Default | Edge cases section. |
| D11 | User edits that add a skill | Allowed, with a visible warning. It is the user's own claim. | Default | The no-fabrication rule applies to the agent, not the user. |
| D12 | Unclear submission result | Shown as **Needs you** with reason `Unconfirmed`. Never shown as Submitted. | Default | Channel outcome model. |
| D13 | Language | English UI. Job descriptions in any language are scored. | Default | |
| D14 | Scale | 100 users at launch and 1,000 at 12 months. A single API instance in v1, with no SignalR backplane. | Default | NFR table. |
| D15 | Availability and recovery | 99.5% uptime. RPO 24 h. RTO 4 h. | Default | NFR table. |
| D16 | Account security | MFA is required once a mailbox is connected. Access token lifetime 15 min. Refresh token lifetime 7 days. | Default | Security section. |
| D17 | Operator access to user data | Only with the user's consent (break-glass). Every access is audited. | Default | Security section. |
| D18 | Audit retention | 1 year, with PII redacted. Spend records are anonymised when an account is deleted. | Default | Resolves the conflict between deleting all data and keeping an append-only ledger. |
| D19 | Cost target scope | The < USD 0.10 per application target covers managed inference only. | Default | Success metrics. |
| D20 | Auto-apply undo | A 10-minute cancel window before any auto-submit runs. | Default | Replaces the vague "undo where possible". |
| D21 | Shared fit report | Shows the role, fit score and reasoning. Never includes CV content. | Default | Growth section. |
| D22 / OQ4 | Limits this document filled in | Confirmed: managed spend ceilings $3 / $7 / $15 per month; Premium fair-use cap 40 Submitted/day; API rate limits 60 req/min per tenant; ≤ 3 browser sessions per worker and ≤ 2 workers; CV upload ≤ 5 MB; passwords ≥ 10 characters | Answered | Sections 3.18, 3.20, 3.21 and 5. The spend ceilings are revisited together with pricing (OQ1). |
| OQ1 | Plan prices vs managed-inference cost | Keep the indicative prices for now. Pricing is defined later. | Answered | Pricing moved to Pending (section 6). |
| OQ2 | Free tier for non-technical users | Free stays BYOK-only for now. To be revisited later. | Answered | Listed under Pending (section 6). |
| OQ3 | Apify budget | Use the Apify free credit by default. The Apify API token and monthly budget are operator configuration, so paid credits can be added without a code change. | Answered | Sections 3.8, 3.11 and 5. All sources stay on Apify. |

## 2. Accepted risks

| Risk | Consequence | Accepted by | Mitigation still in place |
|---|---|---|---|
| **Scraping LinkedIn, Indeed and other boards through Apify** (decision Q7 / F1). This reverses the source document's "no ToS violations" and "LinkedIn/Indeed out of scope" rules. | The operator, not the users, may receive cease-and-desist letters or blocks, and sources may break without warning. Apify actors are third-party and can change or disappear. | Owner (Kamran), 2026-11-22 | No user accounts or credentials are ever used. There is no login-wall circumvention and no automated applying on LinkedIn or Indeed. Each source can be switched off by configuration. Per-source failure alerts. Sourcing stays behind `IJobSource`, so a source can be replaced. |
| **Storing diversity (EEO) answers** (decision Q9). | This is special-category personal data under GDPR-style laws. A breach would be more damaging. | Owner, 2026-11-22 | Explicit separate consent. Answers are encrypted with the per-tenant key. Every answer has a "Prefer not to say" option. The user can edit or delete them at any time. They are never sent to the AI model. They are only used to fill matching form fields. |
| **Apify free tier as the default sourcing budget** (decisions A1 and OQ3). | Sourcing stops for the rest of the month once the credit runs out. | Owner, 2026-11-22 | Shared, once-daily scraping across all tenants. A credit meter with an alert at 80%. Existing postings keep being scored and applied to while sourcing is paused. The operator can switch to a paid Apify token and a higher budget through configuration. |

---

## 3. Refined specification

### 3.1 Overview & Vision

Job-Finder is an AI job-search and application copilot. It finds roles worldwide that genuinely match a candidate's CV, remote or on-site. It tailors the CV and cover letter for each role. It then either applies or queues a one-click approval, always honestly and always under the user's control.

**The problem.** Qualified candidates lose roles because of effort, not ability. Scanning boards, re-reading job descriptions, re-tailoring a CV and re-typing the same details into each applicant tracking system (ATS) is slow. The result is too few applications, or generic ones. Existing "auto-apply" tools send low-quality applications in bulk and break on real ATS flows.

**The product.** A reusable multi-tenant platform where any professional connects a CV, sets preferences, and lets an AI agent run the loop: search -> filter -> evaluate fit -> tailor CV and cover letter -> apply -> track. A review queue keeps the human in control. An autonomy ladder lets trust grow per channel and per employer.

**Brand.** The working name is Job-Finder. A consumer name (for example JobPilot or ApplyWise) is recommended before launch.

### 3.2 Goals & Non-Goals

**Goals**
1. Deliver genuinely matched roles, not volume. Every suggestion has a fit score and reasoning.
2. Cut the time to apply from about 30 minutes of manual work to under 2 minutes of review.
3. Keep the human in control and the system honest: never claim a submission that did not happen.
4. Be profession- and country-agnostic.
5. Run near-free at small scale, and pass heavy AI cost to the user who incurs it.
6. Ship incrementally. Each phase works, is tested and is useful on its own before the next one starts.

**Non-goals**
1. No CAPTCHA solving, MFA bypass or anti-bot evasion, ever. If a site blocks automation, the task is handed to the user.
2. No fabricated experience, skills or answers.
3. Not primarily a résumé builder or a tool for stuffing ATS keywords.
4. No signing in to, or automating, any third-party account. That includes LinkedIn and Indeed applies and any login-walled data.
5. No interview-taking, salary negotiation or impersonation.
6. No tracking of what happens after submission (employer views, interviews, offers, rejections).

### 3.3 Success Metrics

| Metric | Definition | Target (v1) |
|---|---|---|
| Match precision | Share of suggested roles the user marks "relevant" | ≥ 80% |
| Review-to-approve rate | Queued applications the user approves unedited | ≥ 60% |
| Time-to-apply | Median user review time per approved application | < 2 min |
| Submission reliability | Applications that complete without a silent failure | ≥ 95% |
| Honesty integrity | Applications marked Submitted that were actually submitted | 100% (hard invariant; any gap is a Sev-1 defect) |
| Cost per application | Managed-inference AI cost per Submitted application | < USD 0.10 |
| Weekly active tenants | Distinct users running at least one cycle per week | Month-over-month growth |
| Activation | New users reaching their first approved application within 24 h | ≥ 50% |

### 3.4 Target Users & Personas

| Persona | Context | Primary need | Trust posture |
|---|---|---|---|
| Active switcher (e.g. senior .NET engineer, Lahore) | Employed, 30-day notice, wants remote or international roles | High-fit roles surfaced daily; tailored applications with minimal effort. **Must never apply to their current employer.** | Review-first, then auto-apply to trusted employers |
| Open-to-relocate specialist | Will move abroad for the right role | Global sourcing; filtering that accounts for visa and relocation | Review-first; cautious on on-site roles |
| Freelancer / contractor | Short contracts and clients worldwide | Contract and freelance sources; fast outreach emails | Higher autonomy on email outreach |
| Career re-entrant / non-tech | Nurse, accountant or teacher returning to work | Fit explanations in plain language; gentle guidance | Reviews every application |

Nothing in sourcing, scoring or form-filling may hardcode software-industry assumptions. The skills taxonomy, seniority model and screening-question handling are data-driven per tenant.

### 3.5 Feature Set

Tags: **[P0]** to **[P6]** give the delivery phase (see 3.23). **[Later]** means after v1.

**Sourcing & discovery**
- Shared sourcing through Apify actors for all configured sources (including LinkedIn, Indeed, ATS boards and aggregators), with normalisation, duplicate removal and a 30-day freshness window. [P0: one source; P2: all]
- Saved searches; filters for location, remote, salary and visa; per-tenant blocklists for companies and keywords; the current employer blocked automatically. [P2]
- Daily digest of new high-fit roles by email and in-app. [P2]

**Evaluation & tailoring**
- CV parsing into a structured profile. The CV is the single source of truth. [P0]
- Per-role fit score (0–100) with readable reasoning and gap flags. [P2]
- CV tailoring and cover-letter / outreach-email generation in the user's voice. [P3]
- Screening answers drawn from the CV and stored preferences only, never fabricated. [P3]

**Application & delivery**
- Playwright form-filling for common ATS (Greenhouse, Lever, Workable, Ashby), with a human checkpoint. [P0: Greenhouse in DryRun; P3: all four]
- Email applications to listed addresses, with attachments. [P3]
- Autonomy ladder: DryRun -> review queue -> auto-apply within rules. [P0: DryRun; P4: full ladder]
- Hand-off to the user on CAPTCHA, MFA, login walls, LinkedIn/Indeed native apply, or unknown forms. [P3]

**Tracking & insight**
- Application tracker showing agent-side states (Prepared, Queued, Submitted, Needs you, Failed) with a timeline. [P4]
- Real-time progress over SignalR, and notifications on status changes. [P4]
- Analytics: the agent funnel (sourced -> scored -> queued -> submitted) and source quality. [P6]

**Platform**
- Accounts and isolated per-user data. [P1]
- AI provider, job sources and automation behind swappable interfaces. [P1]
- Bring-your-own-key (BYOK) for Anthropic. [P5]
- Browser-extension companion to capture a job from any page. [P6]

### 3.6 The Agentic Loop

One cycle runs per saved search, once a day (or on demand):

1. **Collect:** read the newly sourced postings that match the saved search from the shared posting store.
2. **Pre-filter:** apply cheap rules (location, remote, seniority, language, blocklists, current employer, already-applied ledger).
3. **Rank:** order the remaining postings by recency, source reliability and rule-match strength.
4. **Score:** fit-score the top roles with the model, within the daily scoring cap.
5. **Drop:** discard roles below the fit threshold.
6. **Tailor:** generate a tailored CV, cover letter or email, and screening answers.
7. **Gate:** route each application through the autonomy gate. It is either queued for review or auto-applied, with the 10-minute cancel window.
8. **Apply and record:** submit through a channel and record the exact outcome.

Hard stops always hand control back to the user: CAPTCHA, MFA, login or account-creation walls, payment requests, LinkedIn/Indeed native apply, unanswerable fields, and unconfirmed results. Only a confirmed send is recorded as Submitted.

### 3.7 System Architecture

Layered: SPA -> API -> Application -> Infrastructure -> external services. Dependencies point inward (Clean Architecture). The Application core defines interfaces and Infrastructure implements them, so swapping the AI vendor, a job source or the automation engine means changing an adapter. SignalR pushes live cycle progress to the SPA. Sourcing, scoring and applying run in background workers that read from a durable Postgres-backed job queue, never on the request thread.

### 3.8 Technology Stack

| Layer | Choice | Rationale |
|---|---|---|
| Backend API | .NET 10 / ASP.NET Core, Minimal APIs | Team expertise; performance; first-class DI |
| Architecture | Clean Architecture + SOLID, CQRS-lite where it pays | Testable core, swappable edges, no premature microservices |
| Background work | .NET hosted workers reading a durable Postgres-backed job queue | Long cycles off the request thread; can resume after a restart |
| Persistence | PostgreSQL + EF Core (all environments; Docker locally) | Free and robust; JSONB for flexible per-profession data |
| Realtime | SignalR (single instance in v1) | Live cycle progress and status updates |
| Auth | ASP.NET Core Identity + JWT with refresh-token rotation | Self-hosted, no external identity provider cost |
| Job sourcing | Apify actors behind `IJobSource`; free credit by default, with the operator's Apify token and monthly budget set in configuration | One integration covers many boards; paid credits need no code change |
| Browser automation | Playwright for .NET | Reliable headless browser; strong selector and network control |
| AI | Anthropic Claude: Sonnet-class for scoring and form fields, Opus-class for tailoring and difficult answers; provider-abstracted | Best-in-class JD/CV reasoning |
| Frontend | Latest Angular (standalone, signals, NgRx SignalStore) + Angular Material | Team expertise; accessible components |
| Secrets | ASP.NET user-secrets (dev); Azure Key Vault or Doppler (prod) | Keys never in code or in the database as cleartext |
| Containerisation | Docker; Kubernetes-ready manifests | Portable |
| Hosting | Oracle Cloud Always-Free ARM, Hetzner, or Azure Container Apps | Fits the ~USD 20/month budget |
| CI/CD | GitHub Actions | Free tier |
| Observability | Serilog + OpenTelemetry; Seq/Grafana (self-hosted) | Structured logs and traces across the loop |

### 3.9 Core Modules & Provider Abstractions

| Interface | Responsibility | Example implementations |
|---|---|---|
| `IAgentService` | Orchestrates one cycle (a state machine over the loop stages) | `DefaultAgentService` |
| `IAIProvider` | Chat/completion, structured output, tool use | `ClaudeProvider`; later `OpenAiProvider`, `LocalLlmProvider` |
| `IJobSource` | Fetch normalised postings from one source | `ApifyJobSource` (one configuration per actor) |
| `ICvProfileService` | Parse a CV into a structured profile and keep it current | `PdfCvParser` + `AiProfileExtractor` |
| `IMatchEngine` | Score a job against the profile, with reasoning | `RulePreFilter` (cheap gate) + `AiMatchEngine` |
| `IContentGenerator` | Tailor the CV; write the cover letter or outreach email | `AiContentGenerator` (conditioned on the user's voice) |
| `IApplicationChannel` | Submit one application through one channel | `EmailChannel`, `WebFormChannel`, `AtsChannel:Greenhouse` (and Lever, Workable, Ashby) |
| `IBrowserAutomationService` | Drive a page: navigate, fill, upload, read | `PlaywrightAutomationService` |
| `ICredentialVault` | Store and retrieve mailbox OAuth tokens and BYOK keys, encrypted | `KeyVaultCredentialVault`, `DpapiLocalVault` |
| `IEntitlementService` | Enforce plan daily limits, caps and the spend ceiling | `PlanEntitlementService` |
| `IApplicationTracker` | Persist the application lifecycle | `EfApplicationTracker` |
| `INotificationService` | Email, in-app and SignalR events | `CompositeNotificationService` |

**Layering:**
- **Domain:** entities, value objects, invariants; no dependencies.
- **Application:** use cases, orchestration, validation, interfaces.
- **Infrastructure:** EF Core, Playwright, the Claude client, Apify, email.
- **Api:** Minimal API endpoints, SignalR hubs, auth.

The agent loop lives in Application. Nothing in Domain knows the model vendor, the browser engine or the job-source vendor.

### 3.10 AI Design & Prompt Strategy

The model is used for judgment (fit, tailoring, answering questions), never for control flow or submission. Those stay in deterministic .NET code. Every model call returns structured JSON, which is validated against a schema before the loop acts on it.

**Model routing**

| Task | Model | Why |
|---|---|---|
| Cheap pre-filter (obvious mismatch) | Rules / embeddings, no LLM | Avoid paying to reject junk |
| Fit scoring + reasoning | Sonnet-class | Good enough, cheaper, high volume |
| CV tailoring, cover letters, difficult screening answers | Opus-class | Highest-quality writing and judgment |
| Interpreting form fields during an apply | Sonnet-class + tool use | Fast, structured, repeated |

**Structured outputs.**
- Schemas: `MatchResult { score, reasons[], gaps[], recommend }`, `TailoredCv`, `ScreeningAnswer { question, answer, sourceEvidence }`.
- If a response violates its schema, reject it and retry, at most 2 times. After that, the item becomes Needs you.

**Tool-use loop for form-filling.**
- The model is given the tools `read_field`, `fill_field`, `select_option` and `needs_human`, plus the page's accessibility tree as text.
- The model proposes actions; .NET executes them through Playwright.
- The loop always halts before the final submit control. Submission is then gated by the autonomy level, never by the model.

**Cost discipline.**
- Prompt-cache the system prompt and the tenant's CV.
- Batch fit scoring where latency allows.
- Cap tokens per task.
- Store results so re-runs don't pay again.
- Each tenant has a spend meter with a hard monthly ceiling (section 3.21).

**Honesty in prompts.**
- System prompts forbid inventing experience.
- Screening questions may be answered only from the evidence provided. Otherwise the model returns `needs_human`.
- The evidence behind each answer is shown in the review queue.
- EEO answers are never sent to the model.

### 3.11 Job Sourcing Strategy

All sourcing goes through **Apify actors** behind `IJobSource`, one configured actor per source. This covers LinkedIn, Indeed, ATS public boards (Greenhouse, Lever, Ashby, Workable, SmartRecruiters), aggregators and niche boards. This is an accepted risk (section 2).

**Rules that still apply:**
- **No credentials.** Actors never run with a user's account, cookies or login. There is no login-wall circumvention.
- **Shared sourcing.** Each source is scraped **once per day** for all tenants. Results go into a shared posting store, not a per-tenant copy.
- **Budget.** By default, sourcing runs within the Apify free-tier monthly credit.
  - The Apify API token and the monthly sourcing budget (USD) are operator settings, stored as secrets and configuration rather than in code. To use paid credits, the operator sets a paid account's token and raises the budget. No code change or redeploy of the core is needed.
  - Usage is metered against the configured budget. The operator is alerted at 80% of it.
  - At 100%, sourcing pauses until the credit resets or the budget is raised. Scoring and applying continue on the postings already stored.
- **Off switch.** Each source has a configuration flag so it can be disabled immediately (for example after a block or a cease-and-desist).

**Pipeline.**
1. Fetch.
2. Normalise to a canonical `JobPosting`.
3. Remove duplicates:
   - a content hash of company + title + location + a description shingle, **and**
   - the same company + normalised title within 30 days.
4. Freshness: drop postings older than 30 days, and postings the source reports as closed.
5. Store.

Per-tenant rule pre-filtering and AI scoring happen in the agentic loop.

**Apply-path classification.** Each posting records how it can be applied to:

| Apply path | Handled by |
|---|---|
| External ATS URL | Web-form channel |
| Email address | Email channel |
| LinkedIn Easy Apply / Indeed Apply / unknown | Shown to the user as **Needs you**, with a link to apply themselves. Never automated. |

### 3.12 Application Channels

Each channel implements `IApplicationChannel` and reports a precise, truthful outcome.

**Channel outcomes**

| Outcome | Meaning |
|---|---|
| `Submitted` | Confirmed (definition below) |
| `QueuedForReview` | Waiting for the user's approval |
| `HandedToHuman(reason)` | Reason is one of `Captcha`, `Mfa`, `LoginWall`, `NativeApply`, `UnknownField`, `Unconfirmed`, `Payment` |
| `Failed(reason)` | The attempt failed |

The loop never upgrades a hand-off to Submitted.

**When an application counts as Submitted**

| Channel | Confirmation required |
|---|---|
| Web form | The site displays its confirmation page or message after submit |
| Email | The mail server (SMTP, Gmail API or Microsoft Graph) accepts the message |

Anything less becomes `HandedToHuman(Unconfirmed)`: for example a timeout or an error after clicking submit.

**Email channel.**
- Composes a tailored message in the user's voice, with the CV and an optional cover letter attached.
- Sent through the user's connected mailbox (Gmail / Microsoft Graph OAuth, **send-only scopes**) or a platform relay.
- **Checkpoint:** the user approves the draft. At higher autonomy, the email is sent automatically to employer addresses the user has whitelisted.

**Web-form / ATS channel.**
- Playwright loads the public apply page.
- Known ATS (Greenhouse, Lever, Workable, Ashby) have tuned adapters. Other forms use the generic field mapper.
- The channel fills every field, pauses before the final submit, and shows a screenshot of the filled form for one-click approval.
- At higher autonomy on trusted ATS, it submits automatically after the cancel window.

**Hard stops (always).** These stop the apply and hand it to the user with context. There is never a bypass.
- CAPTCHA
- MFA
- a login or account-creation wall
- a payment request
- LinkedIn / Indeed native apply
- any field the model cannot answer from evidence (the model returns `needs_human`)

**Screening questions.**
- **Factual questions:** answered from the CV and stored preferences (notice period, work authorisation, salary expectation, "how did you hear about us").
- **EEO questions:** answered from the stored `EeoAnswers`. If a question has no stored answer, it becomes Needs you.
- **Free-text motivation questions:** drafted, and always shown for review unless the user has pre-approved that employer.

**Idempotency and duplicates.**
- A unique ledger index on `(TenantId, JobHash)` and on `(TenantId, CompanyKey, NormalisedTitle, 30-day window)` prevents applying to the same job twice.
- Retries are safe: a half-finished apply resumes or is marked Failed, and is never silently re-submitted.

### 3.13 CV Intelligence & Matching Engine

The CV is the single source of truth. Nothing the agent says about the candidate exists outside the CV and the user's stored preferences.

**Parsing.**
- Input: PDF or DOCX, up to 5 MB, text-based. Scanned or image-only PDFs and password-protected files are rejected with a clear message. Uploads are malware-scanned.
- The file is converted to text, then the model extracts a `CandidateProfile`:
  - identity and contact
  - summary
  - skills (with proficiency and years)
  - experience
  - education
  - certifications
  - links
  - work authorisation
  - **current employer**
  - preferences
- The raw file and the structured profile are both stored. The user can correct extraction errors.
- One active CV per user. Each re-upload or edit creates a new profile version.

**Scoring.**
- `IMatchEngine` returns a 0–100 score with:
  - matched requirements
  - gaps
  - seniority fit
  - location / remote fit
  - a one-paragraph rationale
- Default threshold: **70** (configurable per tenant).
- Scores and reasons are stored and shown. The user can override them.

**Tailoring (honest).**
- `IContentGenerator` reorders and rephrases real profile content. The agent never adds skills or experience.
- Output: a tailored CV, a cover letter or outreach email, and a diff the user can inspect.
- If the user's own edit introduces a skill that isn't in the profile, it is allowed but flagged with a warning before approval.

**Screening answers.** Every generated answer carries `sourceEvidence`. With no evidence, the engine returns `needs_human`.

### 3.14 Autonomy Levels & Human-in-the-Loop

Each tenant sets one global level and can override it per channel or per employer. The default is DryRun. The level only rises when the user raises it.

1. **DryRun:** the agent prepares everything and submits nothing. (Default.)
2. **Review-first:** the agent fills forms and drafts emails, then pauses for one-click approval of each application.
3. **Auto-apply (scoped)** (Standard and Premium plans): submits automatically only to whitelisted channels and employers, and only above the user's threshold. Everything else falls back to review.
4. **Auto-apply (broad)** (Premium plan): submits automatically to any ATS or email posting above the threshold, within daily limits.

**Cancel window.** Every auto-submit is held for **10 minutes**, with an instant notification. The user can cancel it during that time.

**Approval queue.** Each item shows:
- the role, fit score and reasoning
- the tailored-CV diff
- the drafted message or a screenshot of the filled form
- the evidence behind each answer

Actions: approve, edit-then-approve, skip, block employer. Items expire after **7 days**.

**Invariants at every level**
- Nothing is reported as Submitted unless the channel confirmed it (section 3.12).
- Hard stops always return to the user.
- Daily limits, caps and the spend ceiling are enforced in code (`IEntitlementService`), never in prompts.
- Every action is audit-logged with its inputs, model outputs and outcome (PII redacted; section 3.18).

### 3.15 Data Model

All tenant data carries a `TenantId` and is isolated by EF Core global query filters. Sensitive columns are encrypted at rest. In v1, tenant and user are 1:1. `JobPosting` is shared across tenants, not tenant-scoped.

| Entity | Purpose | Key relationships |
|---|---|---|
| `Tenant` / `User` | Account, auth, plan, autonomy settings, timezone | 1:1 in v1; owns everything below |
| `CandidateProfile` | Structured CV, raw file reference, preferences, current employer | 1 active per user; versioned |
| `EeoAnswers` | Optional stored diversity answers, with a consent timestamp | 0..1 per user; encrypted |
| `JobPosting` | Normalised shared role: source, apply path, dedup keys, status (open/closed) | Shared; sourced via `JobSource` |
| `JobSource` | An Apify actor configuration, enabled flag, last run, credit use | Many `JobPosting` |
| `SavedSearch` | Query, filters, schedule | Many per user; drives cycles |
| `Blocklist` | Blocked companies and keywords (includes the current employer, added automatically) | Many per user |
| `MatchResult` | Score, reasons, gaps per (profile version, posting) | Links profile and posting |
| `Application` | One attempt: channel, status, profile version, timestamps | 1 per (user, posting); many `ApplicationEvent` |
| `ApplicationEvent` | Agent lifecycle log (prepared, queued, cancel-window, submitted, handed-off, failed) | Many per application |
| `GeneratedArtifact` | Tailored CV, cover letter or answers, with evidence | Many per application |
| `Credential` | Mailbox OAuth tokens and the BYOK key (vault-backed) | Many per user |
| `AgentCycle` | One run of the loop: inputs, cost, outcome counts | Many per user; audit trail |
| `SpendLedger` | Per-tenant AI cost accounting | Append-only; anonymised when the account is deleted |
| `UsageCounter` | Submitted count per tenant per day | Enforces the plan daily limit |
| `AuditLog` | Who or what did what, when (PII redacted) | Kept for 1 year |

**Application status** is a controlled enum with a strict state machine. Tracker-visible states:
- `Prepared`
- `Queued`
- `Submitted`
- `NeedsYou` (with a reason)
- `Failed` (with a reason)

Skipped and expired items leave the tracker and are kept in the audit history.

`MatchResult` and `GeneratedArtifact` are cached so re-runs don't pay the model again.

### 3.16 API Surface

REST for commands and queries, and SignalR for live progress.
- All endpoints are JWT-authenticated and tenant-scoped.
- Long operations return `202 Accepted` with a cycle id.

| Area | Endpoints | Purpose |
|---|---|---|
| Auth | `POST /auth/register`, `/auth/login`, `/auth/refresh`, `/auth/mfa/*` | Identity, JWT rotation, TOTP |
| Profile | `POST /profile/cv`, `GET/PUT /profile`, `GET/PUT/DELETE /profile/eeo` | Upload and parse the CV; edit the profile; diversity answers |
| Searches | `GET/POST/PUT/DELETE /searches`, `GET/PUT /blocklist` | Saved searches, schedules, blocklists |
| Jobs | `GET /jobs`, `GET /jobs/{id}` | Browse sourced and scored roles |
| Matching | `POST /jobs/{id}/score` | On-demand fit score |
| Cycles | `POST /cycles/run`, `GET /cycles/{id}` | Start a loop; get status, cost and outcomes |
| Queue | `GET /applications/queue`, `POST /applications/{id}/approve`, `/skip`, `/edit`, `/cancel` | The review queue and the cancel window |
| Applications | `GET /applications`, `GET /applications/{id}` | Tracker and timeline |
| Settings | `GET/PUT /settings/autonomy`, `/settings/mailbox`, `/settings/byok`, `/settings/notifications` | Autonomy, mailbox connection, BYOK |
| Usage | `GET /usage` | Daily limit remaining, spend meter |

**SignalR hub (`/hub/agent`) events:**
- `CycleStarted`
- `JobsSourced{count}`
- `JobScored{jobId,score}`
- `ApplicationQueued{appId}`
- `AutoSubmitPending{appId,cancelUntil}`
- `ApplicationSubmitted{appId}`
- `NeedsHuman{appId,reason}`
- `CycleCompleted{summary}`

**Conventions:**
- OpenAPI generated.
- Errors use RFC 9457 problem details.
- Idempotency keys on approve and apply.
- Cursor pagination.
- Per-tenant rate limits (section 5).

### 3.17 Frontend & UX

Angular (standalone components, signals, NgRx SignalStore, typed reactive forms) with Angular Material. The experience is a calm control room: the agent works in the background and only surfaces decisions that need the user.

**Key screens**
- **Onboarding:** upload CV, confirm the parsed profile (including current employer), set target roles, locations and remote preference, choose the autonomy level, optionally answer the diversity questions. Goal: first approved application within 24 h.
- **Dashboard:** live cycle status, today's high-fit roles, queue count, daily limit remaining, spend meter.
- **Review queue:** the core screen. Approve, edit, skip, block. Pending auto-submits show a cancel countdown.
- **Applications tracker:** table or kanban by agent state, with a timeline.
- **Searches:** saved searches, schedules, filters, blocklists.
- **Settings:** autonomy per channel and employer, mailbox connection, BYOK key, diversity answers, notifications.

**UX principles**
- Honesty is visible: statuses read exactly as they are (`Submitted`, `Needs you`, `Failed`).
- Every AI output can be explained and edited before it goes out.
- Mobile-responsive. The review queue works on a phone.
- Optimistic UI reconciled with SignalR; skeleton loaders.
- Accessibility to WCAG 2.1 AA.

### 3.18 Security, Privacy & Credential Vault

**Credential vault.**
- Holds mailbox OAuth tokens and BYOK keys only. **No third-party site logins are stored.**
- Production: Azure Key Vault or Doppler, with envelope encryption using per-tenant data keys.
- Secrets are decrypted only in memory and are never logged.

**Data protection.**
- TLS everywhere.
- AES-256 at rest for sensitive columns (profile PII, `EeoAnswers`, tokens).
- CV files are kept in private object storage, reached through signed URLs that expire after 15 minutes.
- PII never appears in URLs, and only the minimum a task needs is sent to the model.
- `EeoAnswers` are never sent to the model.

**AuthN / AuthZ.**
- ASP.NET Core Identity.
- Access tokens last **15 min**; refresh tokens last **7 days** and rotate on every use, with reuse detection.
- Passwords: minimum 10 characters, hashed with PBKDF2 or Argon2, checked against a breached-password list.
- Lockout after **5** failed attempts for 15 min.
- TOTP MFA: optional, but **required once a mailbox is connected**.
- Every query is tenant-filtered at the data layer.

**Operator access.**
- No routine access to tenant data.
- Break-glass access requires the user's in-app consent, lasts at most 24 h, and is recorded in the audit log.

**Logging.**
- Structured logs with PII redaction (email, phone, CV text, tokens).
- Audit logs are kept for **1 year**, are append-only, and can be exported by the user.

**Mailbox scope.** Send-only scopes. The user can revoke access at any time. The platform never reads mail.

**Privacy rights.**
- Export my data.
- Delete my account and all artifacts within 30 days. `SpendLedger` rows are anonymised, not deleted.
- A data-processing notice explains that JD and CV text goes to the AI provider.
- Separate consent is collected for each connected mailbox and for diversity answers.

**App security hygiene.**
- Input validation and output encoding.
- Anti-CSRF on any cookie flow.
- Rate limiting.
- Dependabot and secret scanning in CI.
- Least-privilege service identities.
- No secrets in the repo or the client bundle.

### 3.19 Compliance & Ethical Guardrails

These are product-defining constraints, enforced in code and prompts.

- **No bypass of security controls.** No CAPTCHA solving, MFA defeat or bot-detection evasion. Blocked automation becomes a hand-off.
- **No use of third-party accounts.** The system never signs in to, or automates, any user's account on another site. Job data is collected through Apify actors without user credentials. Scraping sources whose terms prohibit it is an accepted operator risk (section 2), with a per-source off switch.
- **No fabrication by the agent.** The agent never invents experience, skills, employers, dates or answers. Edits the user adds themselves are allowed and flagged.
- **Truthful reporting.** Submitted only on confirmation (section 3.12). "Prepared", "Queued", "Needs you" and "Failed" are distinct, visible states.
- **Human authority.** Irreversible actions need approval unless the user has raised autonomy for that scope. Auto-submits keep a 10-minute cancel window. Hard stops always return control.
- **Never alert the current employer.** The current employer and its known affiliates are blocked automatically.
- **Spend consent.** No managed-inference spend beyond the tenant's ceiling. BYOK spend is on the user's own key.
- **Data rights.** Users can export and delete everything, revoke the mailbox, and delete their diversity answers.

These guardrails are tested, not just documented. The test suite asserts that the system:
- refuses to bypass a CAPTCHA,
- refuses to fabricate an answer that has no evidence,
- never marks a hand-off or an unconfirmed result as Submitted,
- never applies to a blocked or current employer,
- never sends `EeoAnswers` to the model.

### 3.20 Multi-Tenancy, Scalability & Performance

**Tenancy model.** Shared database and shared schema, with row-level isolation by `TenantId`. Job postings are shared and not tenant-scoped.

**Scaling the loop.**
- The API is stateless. v1 runs a single instance, so SignalR needs no backplane. Scaling out later requires a SignalR backplane (Redis or Azure SignalR).
- Workers consume a durable Postgres-backed queue.
- Playwright runs in separate worker containers: at most **3** concurrent browser sessions per worker, at most **2** workers in v1.

**Performance targets.** See the NFR table (section 5).

**Resilience.**
- Polly retries with exponential backoff (3 attempts) and circuit breakers around Apify, the model, email and automation.
- A failed stage never corrupts the ledger.
- Cycles resume from the last completed stage after a restart.

**Capacity knobs.**
- Per-tenant caps: roles scored per day, Submitted per day by plan.
- A global limit on concurrent browser sessions.
- The spend ceiling.

When these are reached, work waits in the queue instead of failing.

### 3.21 Cost Model & Monetization

**Operator cost.** Free-tier or cheap compute, self-hosted or free-tier PostgreSQL, the Apify free tier, GitHub Actions and free object storage. Target: about USD 20/month at small scale.

**AI cost modes.**
1. **BYOK:** the user's own Anthropic key. Operator AI cost = 0.
2. **Managed inference:** the platform's key, metered against a per-plan monthly spend ceiling (below). Costs are kept down by model routing, prompt caching, rule pre-filters and the daily caps.

**Plans** (prices and spend ceilings are indicative; pricing and payments are pending, see section 6)

| Plan | Price / month | Submitted applications / day | Inference | Includes |
|---|---|---|---|---|
| Free | $0 | 1 | BYOK only | Core sourcing, fit scoring, review-first apply |
| Basic | ~$6 | 5 | Managed (ceiling $3/month) or BYOK | + all sources, saved searches, tracker, digests |
| Standard | ~$14 | 15 | Managed (ceiling $7/month) or BYOK | + scoped auto-apply, analytics (P6) |
| Premium | ~$29 | Fair-use, up to 40 | Managed (ceiling $15/month) or BYOK | + broad auto-apply |

**Plan limits.**
- The whole application requires sign-in. Anonymous visitors get the landing page and a DryRun demo only.
- `IEntitlementService` checks limits server-side before every submit.
- **Quota unit:** one Submitted outcome. Prepared, queued, skipped, handed-off, cancelled and failed applications don't count.
- **Reset:** daily at 00:00 in the user's timezone.
- **Warnings:** the dashboard shows remaining applications and warns on the last one of the day.
- **At the limit:**
  - New submits are blocked with an upgrade prompt.
  - Sourcing, scoring and tailoring keep running.
  - Pending auto-submits stay queued for the next day, not dropped.
- **When the managed-inference ceiling is reached:**
  - Model work stops for the rest of the month, unless the user switches to BYOK.
  - The managed ceiling may become the binding limit before the daily limit. This is revisited when pricing is defined (section 6).
- **Plan changes** take effect immediately. A downgrade never cancels Submitted applications. If the new limit is already used up for the day, further submits wait until the next reset.

The plan daily limit is separate from the per-tenant scoring cap (50 roles/day, all plans).

**Open-core.** Ship the core as open source; monetise managed hosting, inference and premium features. The licence is pending (section 6).

### 3.22 Deployment, DevOps & Observability

**Containers.** API, background workers and browser workers are separate images. Docker Compose for local development and the MVP; Kubernetes or Azure Container Apps when load justifies it. Browser workers ship with Playwright's browsers baked into the image.

**CI/CD (GitHub Actions).**
- **On PR:** build, unit and integration tests, lint, dependency and secret scanning.
- **On merge to main:**
  1. Build and push images.
  2. Run EF Core migrations.
  3. Deploy to staging and smoke-test.
  4. Promote to production after approval.

**Configuration.** Twelve-factor config from the environment. Secrets are injected at runtime. Each environment has its own credentials.

**Observability.**
- Serilog with a correlation id per cycle.
- OpenTelemetry traces covering API -> worker -> model -> browser.
- **Metrics:**
  - cycle duration
  - jobs sourced and scored
  - apply success rate
  - Needs-you rate by reason
  - tokens and cost per tenant
  - Apify credit used
  - queue depth
- **Alerts:**
  - spikes in apply failures
  - a tenant's spend at 80% of its ceiling
  - Apify credit at 80%
  - a source failing 3 days in a row
  - **any honesty-invariant violation (page immediately)**

**Backups.**
- Daily automated PostgreSQL backups, kept for 14 days. The restore is tested monthly.
- Object-storage versioning for CV files.
- RPO 24 h, RTO 4 h. Migrations are forward-only and reviewed.

**Environments.** Local -> Staging -> Production. Staging uses seeded demo data. Every non-production environment is **forced to DryRun**, so no real application can be sent from it.

### 3.23 Phased Delivery Roadmap

Delivery is strictly incremental: no phase starts until the previous one works and has tests. The owner builds alone; dates are pending.

| Phase | Focus | Key deliverables | Exit criteria | Status |
|---|---|---|---|---|
| 0 - MVP loop | Prove the loop | Minimal API + Angular page, one Apify source, CV parsing, Playwright applier (Greenhouse), DryRun only | One end-to-end DryRun application from a real CV | Planned |
| 1 - Platform foundation | Accounts + isolation | Identity + JWT + MFA, tenant isolation, PostgreSQL, durable job queue, Clean Architecture skeleton, provider interfaces | Two isolated users; CI green | Planned |
| 2 - Sourcing & matching | Find and rank well | All Apify sources, shared posting store, duplicate removal, rule pre-filter, current-employer block, AI fit scoring, daily digest | Daily high-fit digest per user | Planned |
| 3 - Tailoring & channels | Apply for real | CV tailoring, cover letters, email + web-form channels for 4 ATS, review queue, hand-offs, diversity answers | Review-first application across 3 ATS | Planned |
| 4 - Autonomy & tracking | Trust + visibility | Autonomy ladder, cancel window, SignalR progress, application tracker, notifications | Scoped auto-apply within limits | Planned |
| 5 - Launch | Product + revenue | Plans and daily limits, BYOK, open-core repo, docs site. **Pending:** payments, jurisdictions, licence | First self-hosted users; first paying users once payments land | Planned |
| 6 - Scale & insight | Grow and learn | Funnel and source-quality analytics, browser extension, more sources | Retention and referral metrics tracked | Planned |

### 3.24 Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Scraped sources block Apify or change their markup | Sourcing gaps | Multiple sources, per-source failure alerts, off switch, posting store survives outages |
| Legal or terms exposure from scraping LinkedIn, Indeed and others | Cease-and-desist, blocks | **Accepted risk** (section 2); no user credentials; no automated applying there; per-source off switch |
| Apify free credit runs out | Sourcing paused for the rest of the month | Once-daily shared scraping, credit meter, 80% alert, existing postings still usable |
| A bad auto-apply burns a real opportunity | Lost role, lost trust | DryRun default, evidence-backed answers, review queue, 10-minute cancel window, per-employer scoping |
| Applying to the current employer | Alerts the employer | Automatic current-employer and affiliate block; tested invariant |
| Model cost exceeds the budget | Unprofitable | BYOK, routing, prompt caching, pre-filters, per-plan spend ceilings |
| Breach of CV, PII, diversity answers or tokens | Severe trust and legal damage | Vault + envelope encryption, no site logins stored, diversity answers never sent to the model, least privilege, redacted logs |
| Model fabricates an answer | Dishonest application | Evidence requirement, schema validation, `needs_human`, tested refusals |
| "Submitted" shown when it wasn't | Breaks the core promise | Confirmation-only Submitted, Unconfirmed -> Needs you, invariant tested and alerted |
| Flaky browser automation | Throughput collapse | Session pooling, retries, circuit breakers, resumable cycles |
| Solo maintainer | Slow delivery | Strict phasing, lean scope per phase, open-source contributors |

### 3.25 Growth

**The pitch.**
1. Fit quality: match reasoning candidates agree with.
2. Honesty: it never lies about a submission and never fabricates.
3. Control: an autonomy ladder instead of blasting applications out.

**Distribution.**
- Open-core on GitHub with a one-command self-host.
- Developer-focused content about building the agent with Claude + .NET + Angular.
- Product Hunt, Show HN, remote-work communities, dev.to.
- A landing page with a live DryRun demo.
- Win one niche first (remote .NET/dev roles) before broadening.

**Virality and retention.**
- A shareable fit report for any role: role, score and reasoning only, **never CV content**.
- Referral credit (managed-inference credits).
- A daily or weekly digest of good matches.

**First milestone.** 100 self-hosted or Free users, plus a handful of success stories reported by users.

### 3.26 Glossary

| Term | Meaning |
|---|---|
| ATS | Applicant Tracking System (Greenhouse, Lever, Workable, Ashby, ...) |
| Agentic loop | The cycle: collect -> filter -> rank -> score -> tailor -> gate -> apply -> record |
| Apify actor | A third-party scraper run on the Apify platform; one configured per job source |
| Autonomy level | How much the agent may do without approval (DryRun -> broad auto-apply) |
| BYOK | Bring your own key: the user supplies their own Anthropic API key |
| Cancel window | The 10 minutes before an auto-submit runs, during which the user can cancel |
| DryRun | Prepares everything, submits nothing |
| Fit score | 0–100 match of a role to the profile, with reasoning |
| Hard stop | A point where the agent must hand off (CAPTCHA, MFA, login wall, payment, native apply, unknown field) |
| Hand-off / Needs you | Returning control to the user with context |
| Submitted | Confirmed by the channel (section 3.12), and nothing less |
| Tenant | One user's isolated data boundary (1:1 with user in v1) |

---

## 4. Edge cases

| # | Scenario | Expected behaviour |
|---|---|---|
| E1 | The same job is posted on several sources, or reposted with an edited description | Duplicate removal by hash plus company + normalised title within 30 days. One application at most. |
| E2 | The posting is LinkedIn Easy Apply or Indeed Apply only | Needs you (`NativeApply`) with the link. Never automated. |
| E3 | The posting belongs to the user's current employer or a known affiliate | Removed by the pre-filter. Never shown, scored or applied to. |
| E4 | The user approves a queue item after the posting has closed | The approval is refused; the item is shown as "Posting closed" and moved out of the queue. |
| E5 | The user approves an item prepared from an older profile version | It must be re-prepared with the current profile before approval. |
| E6 | A queue item is untouched for 7 days | It expires and leaves the queue; this is recorded in the audit log. |
| E7 | The daily submit limit is reached in the middle of a cycle | The current submit finishes. Later submits wait until the next daily reset. Scoring and tailoring continue. |
| E8 | The managed spend ceiling is reached in the middle of a cycle | The model call in flight finishes. No new model calls this month unless the user switches to BYOK. Already-prepared items can still be submitted. |
| E9 | The Apify credit or the configured sourcing budget runs out | Sourcing pauses until the credit resets or the operator raises the budget or sets a paid token. The operator is alerted. Cycles use the postings already stored. |
| E10 | Submit was clicked but no confirmation appeared (timeout or error page) | `HandedToHuman(Unconfirmed)` is shown as Needs you. It is never Submitted and never retried automatically. |
| E11 | The user cancels during the 10-minute window | Nothing is sent. The item returns to the queue and no daily limit is used. |
| E12 | The user lowers autonomy while auto-submits are pending | Pending auto-submits revert to the review queue immediately. |
| E13 | The user revokes the mailbox while emails are queued | The queued emails become Needs you (`MailboxDisconnected`). |
| E14 | The user deletes their account while work is in flight | Pending submits are cancelled. Data is deleted within 30 days. `SpendLedger` is anonymised. |
| E15 | A form asks a diversity question with no stored answer | Needs you. The user can answer it and optionally save the answer. |
| E16 | A form forces a salary number outside the user's stated range | Needs you. The agent never picks a number. |
| E17 | The CV is scanned, image-only, password-protected or over 5 MB | The upload is rejected with a specific reason. |
| E18 | The user's edit adds a skill the CV doesn't have | Allowed. A warning is shown before approval and the edit is recorded in the audit log. |
| E19 | A worker crashes in the middle of an apply | The job is retried from its last stage. A form already submitted is never re-submitted. An unclear outcome becomes Unconfirmed. |
| E20 | The model returns invalid JSON twice | The item becomes Needs you (`ModelError`). The cycle continues with the other items. |
| E21 | The job description is not in English | It is scored as normal. The reasoning is written in English. |
| E22 | The user downgrades their plan after already submitting more than the new daily limit today | Nothing is cancelled. Further submits wait until the next reset. |

## 5. Non-functional requirements

| Category | Requirement | Target / measure |
|---|---|---|
| Performance | API query latency | p95 < 300 ms |
| Performance | Full cycle for one saved search (score up to 50 roles + prepare the top matches) | p95 < 10 min |
| Performance | SignalR event delivery | < 1 s from the event |
| Performance | Review queue first load on mobile (4G) | < 2 s |
| Scale | Users | 100 at launch; 1,000 at 12 months |
| Scale | Browser concurrency | ≤ 3 sessions per worker, ≤ 2 workers in v1 |
| Scale | Sourcing | Each source once per day, shared across all tenants |
| Availability | Uptime | 99.5% monthly |
| Recovery | RPO / RTO | 24 h / 4 h |
| Recovery | Backups | Daily, kept 14 days, restore tested monthly |
| Security | Access / refresh token lifetimes | 15 min / 7 days, rotating, with reuse detection |
| Security | MFA | Optional; required once a mailbox is connected |
| Security | Lockout | 5 failed attempts, then locked for 15 min |
| Security | Uploads | PDF/DOCX ≤ 5 MB, malware-scanned |
| Security | Encryption | TLS 1.2+ in transit; AES-256 at rest for sensitive columns; per-tenant data keys |
| Security | Operator access | Break-glass only, with user consent, ≤ 24 h, audited |
| Audit | Audit log | Every agent action and admin access; append-only; PII redacted; kept 1 year; user-exportable |
| Privacy | Account deletion | Completed within 30 days; spend records anonymised |
| Privacy | Diversity answers | Explicit consent, encrypted, never sent to the AI model |
| Rate limits | API per tenant | 60 requests/min (burst 120) |
| Rate limits | Auth endpoints | 10 requests/min per IP |
| Rate limits | Cycles | At most 1 on-demand run per saved search per hour |
| Cost | Managed inference | < USD 0.10 per Submitted application |
| Cost | Infrastructure | ~USD 20/month at small scale |
| Cost | Sourcing | Apify free-tier credit by default; configurable monthly budget; alert at 80% |
| Accessibility | UI | WCAG 2.1 AA |
| Integrity | Honesty invariant | 100%; any violation pages the operator immediately |

## 6. Pending / deferred

| Item | Deferred to | Notes |
|---|---|---|
| Pricing: plan prices and managed-inference spend ceilings | Phase 5 (Launch) | At USD 0.10 per application, the daily limits cost more in managed inference than the indicative prices. Set prices, ceilings and limits together. |
| Free tier for non-technical users | Phase 5 (Launch) | Free is BYOK-only for now. Revisit whether Free gets a small managed allowance. |
| Payment processor and billing currency | Phase 5 (Launch) | Stripe is not available to Pakistani merchants; choose before charging anyone |
| Target jurisdictions and compliance (GDPR, PIPEDA, Pakistan PDPA, US), data residency, AI-provider processing agreement | Phase 5 (Launch) | Until then, the GDPR-style rights in section 3.18 apply to everyone |
| Open-source licence for the open-core repo | Phase 5 (Launch) | MIT, Apache-2.0 or AGPL |
| Delivery dates | To be set by the owner | Solo build |

## 7. Open questions

None. Items postponed by the owner are listed in section 6.
