# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

JobsFinder: ASP.NET Core 10 API (clean architecture, EF Core + PostgreSQL) with an Angular 21 front end in `client/`.

## Commands

Run .NET commands from the repo root and npm commands from `client/`.

```bash
# .NET
dotnet build                                   # warnings are errors (Directory.Build.props)
dotnet test tests/JobsFinder.UnitTests         # fast, no external deps
dotnet test tests/JobsFinder.IntegrationTests  # needs Docker running (Testcontainers starts PostgreSQL)
dotnet test --filter "FullyQualifiedName~CreateJobHandlerTests.HandleAsync_DuplicateUrl_ReturnsConflict"  # single test
dotnet format --verify-no-changes              # .NET lint/style check (.editorconfig naming + analyzers)
dotnet run --project src/JobsFinder.Api        # http://localhost:5080, OpenAPI at /openapi/v1.json

# EF Core migrations (dotnet-ef is a local tool: run `dotnet tool restore` once)
dotnet ef migrations add <Name> -p src/JobsFinder.Infrastructure -s src/JobsFinder.Api -o Persistence/Migrations
dotnet ef database update -p src/JobsFinder.Infrastructure -s src/JobsFinder.Api

# Angular (client/)
npm start                     # ng serve on :4200, proxies /api -> :5080 (proxy.conf.json)
npm run lint                  # angular-eslint
npm test                      # Vitest via `ng test`, single run
npx ng test --include src/app/features/jobs/state/jobs.store.spec.ts   # single spec file
npm run build
npm run e2e                   # Playwright; starts ng serve itself
npx playwright test e2e/jobs.spec.ts -g "shows the API error"          # single e2e test

# Full stack in Docker
docker compose up --build     # db :5432, api :5080, web :4200 (nginx proxies /api to the api container)
docker compose up db          # just PostgreSQL, for running the API/Angular locally
```

In Development the API applies pending migrations on startup (`Program.cs`). Other environments must run migrations explicitly.

## Backend architecture

`JobsFinder.slnx`: `src/` holds the four layers, `tests/` holds the test projects.

| Project | Contains | References |
|---|---|---|
| `JobsFinder.Domain` | Entities, enums, invariants (`DomainException`) | nothing |
| `JobsFinder.Application` | Use-case handlers, commands/queries, DTOs + mappings, FluentValidation validators, `Result<T>`, repository/UoW interfaces | Domain |
| `JobsFinder.Infrastructure` | `AppDbContext`, EF configurations, migrations, repository implementations | Application |
| `JobsFinder.Api` | Minimal API endpoints, error handling, composition root | Application, Infrastructure |

**Dependency rule:** references only point inward (Api → Infrastructure → Application → Domain). Domain and Application must never reference EF Core, ASP.NET Core or Npgsql. If Application needs something from the outside (persistence, clock, email), define an interface in Application and implement it in Infrastructure. Only `Program.cs` may reference Infrastructure types directly. `TimeProvider` is injected for "now" so tests can control time.

How a request flows, using `Jobs` as the reference feature (copy its shape for new features):
1. `Api/Endpoints/JobEndpoints.cs` binds the request and calls a handler, e.g. `CreateJobHandler.HandleAsync(command, ct)`.
2. The handler (`Application/Jobs/CreateJob.cs`) validates with the injected `IValidator<T>`, checks business rules through `IJobRepository`, creates the entity with `Job.Create(...)`, and calls `IUnitOfWork.SaveChangesAsync`.
3. The handler returns `Result<T>`; `ResultExtensions.ToHttpResult` turns it into 200/201 or into problem details.

There is no MediatR or AutoMapper (both now need commercial licences). Handlers are plain classes registered in `Application/DependencyInjection.cs`. Each new handler must be registered there; validators are picked up automatically from the assembly.

Each use case lives in one file under `Application/<Feature>/` that holds the command/query record, its validator and its handler.

## Backend conventions

- **Naming:** PascalCase types and members, `_camelCase` private fields, `I`-prefixed interfaces, file-scoped namespaces, one feature folder per aggregate. Test names follow `Method_Scenario_Expected`.
- **Async:** every I/O method is `async`, ends in `Async` and takes a `CancellationToken` as its last parameter, passed all the way down to EF. No `.Result`, `.Wait()` or `async void`. Don't add `ConfigureAwait(false)` (ASP.NET Core has no sync context).
- **Entities:** private setters, a private parameterless constructor for EF, and a static `Create` factory that enforces invariants. Ids are `Guid.CreateVersion7()` generated in Domain (`ValueGeneratedNever`). Max lengths are constants on the entity, reused by EF configuration and validators.
- **EF Core:**
  - No lazy loading: never add `UseLazyLoadingProxies` or `virtual` navigation properties. Load related data with `Include` or a projection.
  - Every read query uses `AsNoTracking()` and projects to a DTO with `.Select(XMappings.Projection)` inside the repository. Never return entities from read methods.
  - Track entities only when you will modify them and then call `SaveChangesAsync` once per use case through `IUnitOfWork`.
  - One `IEntityTypeConfiguration<T>` per entity in `Infrastructure/Persistence/Configurations`. No data annotations on Domain entities.
  - Enums are stored as strings. Table names are snake_case plurals (`jobs`).
  - Paging always has a deterministic `OrderBy` + `ThenBy(Id)`.
- **DTO mapping:** do it by hand in `<Feature>Mappings`. Keep one `Expression<Func<Entity, Dto>> Projection` for EF queries and a `ToDto()` extension for in-memory entities, both built from the same expression. DTOs are `sealed record`s in Application; Api has no DTOs of its own.
- **Validation:** request shape and format go in FluentValidation validators in Application, run by the handler, which returns `Error.Validation`. Invariants that must never be broken go in the Domain factory/methods (`DomainException`). Uniqueness/existence checks go in the handler and return `Error.Conflict` or `Error.NotFound`.
- **Errors:** expected failures are `Result<T>` errors, never exceptions. `GlobalExceptionHandler` turns `DomainException` into 400 and anything else into a logged generic 500. All error responses are RFC 9457 problem details with a machine-readable `code` extension (snake_case, e.g. `job_duplicate_url`). JSON enums are strings.
- **Logging:** use `[LoggerMessage]` source-generated methods (see `GlobalExceptionHandler`), never string-interpolated log messages.
- **Analyzers:** `AnalysisLevel=latest-recommended` + `TreatWarningsAsErrors`. Fix warnings; if you must suppress one, do it in `.editorconfig` with a comment explaining why.
- **Tests:**
  - Unit tests cover Domain and Application with hand-written fakes (no mocking library).
  - Integration tests use `ApiFactory` (`WebApplicationFactory<Program>` + Testcontainers PostgreSQL) shared through `[Collection(ApiTestGroup.Name)]` and go through HTTP. Use unique data per test (e.g. GUIDs in URLs) because the database is shared within a run.
  - xUnit `Assert` only (FluentAssertions is commercial now).

## Frontend (`client/`)

Angular 21: standalone components, zoneless change detection, signals, `OnPush`. It follows the newer file naming, so component files have no `.component` suffix (`job-list.ts`, class `JobList`).

```
src/app/
  core/            app-wide singletons: http helpers (problem-details.ts), interceptors, guards
  shared/          reusable presentational components, pipes, directives (no feature state)
  features/<name>/
    data/          <name>.api.ts (HttpClient wrapper, no state) + *.model.ts (mirrors API DTOs)
    state/         <name>.store.ts (NgRx SignalStore) + spec
    pages/         routed components
    components/    feature-only presentational components
    <name>.routes.ts   lazy-loaded from app.routes.ts; provides the feature store
```

**State management:** NgRx SignalStore (`@ngrx/signals`).
- One store per feature, provided in that feature's route `providers`, not `providedIn: 'root'` unless the state really is global.
- Components inject the store and read signals. They never call `*.api.ts` directly.
- Async work uses `rxMethod` + `tapResponse`, and state changes only through `patchState` inside the store.
- Derived values use `withComputed`.
- Turn API errors into messages with `toErrorMessage()`, which reads the problem-details `title`.

**Other conventions:**
- Use relative `/api/...` URLs. The dev proxy and nginx route them to the API.
- `*.model.ts` types must match the C# DTOs, including string enums.
- Use control flow blocks (`@if`/`@for`), `inject()` rather than constructor injection, and `input()`/`output()` signals.
- Unit specs live next to the code (`*.spec.ts`, Vitest).
- Playwright specs live in `client/e2e/` and stub the API with `page.route`, so they don't need the backend. Select elements by role, label or `data-testid`.

## Git and Jira

- Jira project key: **JF**. Workflow: To Do → Ready → In Progress → In Review → QA → Done.
- Branches: `feature/JF-<n>-short-desc`, lowercase kebab-case (e.g. `feature/JF-12-job-search-filters`), cut from `master` (GitHub's default branch; `git switch -c feature/JF-<n>-desc origin/master`).
- Commits: `JF-<n>: imperative summary` (e.g. `JF-12: Add remote-policy filter to job search`). Every commit and PR title carries the issue key so Jira links them automatically.
- Move the Jira issue to In Progress when the branch is created and to In Review when the PR opens.
