# Job-Finder - Features by phase

This folder breaks the [refined specification](../job-finder-product-engineering-documentation.md) into one document per feature, grouped by delivery phase. The refined spec stays the source of truth: if a feature doc and the spec disagree, the spec wins and the feature doc should be fixed.

## Rules
- Phases are delivered strictly in order. A phase starts only when the previous one works and has tests.
- Feature IDs (`F<phase>.<n>`) are stable. When a Jira issue is created for a feature, add its `JF-<n>` key to the feature doc's header table.
- Every feature doc uses the same sections: Summary, User stories, Scope, Business rules, Acceptance criteria, Edge cases, NFRs, Dependencies, Open points.

## Phase 0 - MVP loop
Exit: one end-to-end DryRun application from a real CV.

| ID | Feature |
|---|---|
| F0.1 | [CV upload and parsing](phase-0-mvp-loop/f0.1-cv-upload-and-parsing.md) |
| F0.2 | [Single-source job ingestion (Apify)](phase-0-mvp-loop/f0.2-single-source-job-ingestion.md) |
| F0.3 | [Greenhouse DryRun applier](phase-0-mvp-loop/f0.3-greenhouse-dryrun-applier.md) |
| F0.4 | [MVP loop orchestration and results page](phase-0-mvp-loop/f0.4-mvp-loop-and-results-page.md) |

## Phase 1 - Platform foundation
Exit: two isolated users; CI green.

| ID | Feature |
|---|---|
| F1.1 | [Identity and authentication](phase-1-platform-foundation/f1.1-identity-and-authentication.md) |
| F1.2 | [Tenant isolation](phase-1-platform-foundation/f1.2-tenant-isolation.md) |
| F1.3 | [Durable job queue and background workers](phase-1-platform-foundation/f1.3-durable-job-queue-and-workers.md) |
| F1.4 | [Architecture skeleton and provider interfaces](phase-1-platform-foundation/f1.4-architecture-skeleton-and-provider-interfaces.md) |
| F1.5 | [Audit log and observability baseline](phase-1-platform-foundation/f1.5-audit-log-and-observability.md) |
| F1.6 | [Credential vault](phase-1-platform-foundation/f1.6-credential-vault.md) |
| F1.7 | [CI/CD and environments](phase-1-platform-foundation/f1.7-ci-cd-and-environments.md) |

## Phase 2 - Sourcing & matching
Exit: daily high-fit digest per user.

| ID | Feature |
|---|---|
| F2.1 | [Multi-source Apify sourcing and shared posting store](phase-2-sourcing-and-matching/f2.1-multi-source-apify-sourcing.md) |
| F2.2 | [Normalisation, duplicate removal and freshness](phase-2-sourcing-and-matching/f2.2-normalisation-dedup-and-freshness.md) |
| F2.3 | [Saved searches and filters](phase-2-sourcing-and-matching/f2.3-saved-searches-and-filters.md) |
| F2.4 | [Blocklists and current-employer block](phase-2-sourcing-and-matching/f2.4-blocklists-and-current-employer-block.md) |
| F2.5 | [Rule pre-filter and ranking](phase-2-sourcing-and-matching/f2.5-rule-pre-filter-and-ranking.md) |
| F2.6 | [AI fit scoring](phase-2-sourcing-and-matching/f2.6-ai-fit-scoring.md) |
| F2.7 | [Daily digest](phase-2-sourcing-and-matching/f2.7-daily-digest.md) |

## Phase 3 - Tailoring & channels
Exit: review-first applications across 3 ATS.

| ID | Feature |
|---|---|
| F3.1 | [CV tailoring and cover letters](phase-3-tailoring-and-channels/f3.1-cv-tailoring-and-cover-letters.md) |
| F3.2 | [Evidence-based screening answers](phase-3-tailoring-and-channels/f3.2-evidence-based-screening-answers.md) |
| F3.3 | [Diversity (EEO) answers](phase-3-tailoring-and-channels/f3.3-diversity-eeo-answers.md) |
| F3.4 | [Review queue](phase-3-tailoring-and-channels/f3.4-review-queue.md) |
| F3.5 | [Web-form / ATS channel](phase-3-tailoring-and-channels/f3.5-web-form-ats-channel.md) |
| F3.6 | [Email channel](phase-3-tailoring-and-channels/f3.6-email-channel.md) |
| F3.7 | [Hard stops and hand-offs](phase-3-tailoring-and-channels/f3.7-hard-stops-and-hand-offs.md) |
| F3.8 | [Application ledger and truthful outcomes](phase-3-tailoring-and-channels/f3.8-application-ledger-and-truthful-outcomes.md) |

## Phase 4 - Autonomy & tracking
Exit: scoped auto-apply within limits.

| ID | Feature |
|---|---|
| F4.1 | [Autonomy ladder](phase-4-autonomy-and-tracking/f4.1-autonomy-ladder.md) |
| F4.2 | [Auto-apply and the 10-minute cancel window](phase-4-autonomy-and-tracking/f4.2-auto-apply-and-cancel-window.md) |
| F4.3 | [Live progress (SignalR)](phase-4-autonomy-and-tracking/f4.3-live-progress-signalr.md) |
| F4.4 | [Application tracker](phase-4-autonomy-and-tracking/f4.4-application-tracker.md) |
| F4.5 | [Notifications](phase-4-autonomy-and-tracking/f4.5-notifications.md) |
| F4.6 | [Usage caps and spend ceiling](phase-4-autonomy-and-tracking/f4.6-usage-caps-and-spend-ceiling.md) |

## Phase 5 - Launch
Exit: first self-hosted users; first paying users once payments land.

| ID | Feature |
|---|---|
| F5.1 | [Plans and daily limits](phase-5-launch/f5.1-plans-and-daily-limits.md) (prices pending) |
| F5.2 | [Bring your own key (BYOK)](phase-5-launch/f5.2-bring-your-own-key.md) |
| F5.3 | [Data rights and privacy](phase-5-launch/f5.3-data-rights-and-privacy.md) |
| F5.4 | [Open-core repo and self-host](phase-5-launch/f5.4-open-core-and-self-host.md) (licence pending) |
| F5.5 | [Landing page, DryRun demo and onboarding](phase-5-launch/f5.5-landing-page-demo-and-onboarding.md) |
| F5.6 | [Payments, billing and compliance](phase-5-launch/f5.6-payments-billing-and-compliance-pending.md) (**pending**) |

## Phase 6 - Scale & insight
Exit: retention and referral metrics tracked.

| ID | Feature |
|---|---|
| F6.1 | [Funnel and source-quality analytics](phase-6-scale-and-insight/f6.1-funnel-and-source-quality-analytics.md) |
| F6.2 | [Browser-extension companion](phase-6-scale-and-insight/f6.2-browser-extension.md) |
| F6.3 | [More sources](phase-6-scale-and-insight/f6.3-more-sources.md) |
| F6.4 | [Shareable fit report and referrals](phase-6-scale-and-insight/f6.4-shareable-fit-report-and-referrals.md) |
