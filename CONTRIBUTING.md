# Contributing to Taskdeck

Thanks for your interest in Taskdeck. This file is a friendly onramp for new
contributors: how to get the code running locally, how to run tests, how we
write commits, and how PRs flow through review.

> **Authoritative contributor protocol:** [AGENTS.md](AGENTS.md).
> This file is a practical introduction; `AGENTS.md` is the full rulebook,
> including the definition of done, output expectations, and repo guardrails.
> If anything here conflicts with `AGENTS.md`, `AGENTS.md` wins.

---

## Prerequisites

You need these installed before anything else:

| Tool | Version | Notes |
|------|---------|-------|
| **.NET SDK** | 8.0.x | Backend runtime + tests. Get it from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0). |
| **Node.js** | 24.x (>= 24.13.1 LTS) | Frontend + E2E runtime. Match the version pinned in `frontend/taskdeck-web/package.json` / CI. |
| **Git** | 2.40+ | Any recent build. On Windows, use Git for Windows (see below). |

Optional:

- **Docker Desktop** (or Docker Engine) if you want to run the Docker Compose profile.
- **PowerShell 7+** on Windows if you prefer a modern shell.

---

## Local Setup (Windows, macOS, Linux)

Clone the repo:

```bash
git clone https://github.com/Chris0Jeky/Taskdeck.git
cd Taskdeck
```

### Backend (.NET 8)

From the repo root:

```bash
dotnet restore backend/Taskdeck.sln
dotnet build backend/Taskdeck.sln -c Release
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj
```

The API starts on `http://localhost:5000` with Swagger at `http://localhost:5000/swagger`.

#### JWT Secret

The backend requires a JWT signing secret (at least 32 characters). On first
run, `FirstRunBootstrapper` automatically generates a cryptographically random
secret and saves it to `appsettings.local.json` (which is gitignored). No
manual setup is needed for most developers.

If you prefer to manage the secret yourself, you have two options:

**Option A: `dotnet user-secrets`** (recommended for shared machines)

```bash
cd backend/src/Taskdeck.Api
dotnet user-secrets init   # one-time setup
dotnet user-secrets set "Jwt:SecretKey" "YourSecretKeyAtLeast32CharactersLong!"
```

**Option B: Environment variable**

```bash
export Jwt__SecretKey="YourSecretKeyAtLeast32CharactersLong!"
```

On Windows PowerShell:

```powershell
$env:Jwt__SecretKey = "YourSecretKeyAtLeast32CharactersLong!"
```

Environment variables take precedence over `appsettings.local.json`, which
takes precedence over all `appsettings.*.json` files.

### Frontend (Vue 3 + Vite)

In a second terminal:

```bash
cd frontend/taskdeck-web
npm install
npm run dev
```

The dev server runs on `http://localhost:5173`. Open it in your browser; it
talks to the backend on port 5000 by default.

### Docker (optional)

From the repo root:

```bash
cp deploy/.env.example deploy/.env
```

Edit `deploy/.env` and set BOTH required secrets before starting:

- `TASKDECK_JWT_SECRET` (generate with: `openssl rand -base64 48`)
- `TASKDECK_CONNECTORS_ENCRYPTION_KEY` (generate with: `openssl rand -base64 32`)

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build
```

Reverse proxy: `http://localhost:8080`. Details in
[docs/ops/DEPLOYMENT_CONTAINERS.md](docs/ops/DEPLOYMENT_CONTAINERS.md).

---

## Platform-Specific Notes

### Windows

Windows contributors: read this section before your first commit. These are
real gotchas baked into the repo's workflow.

- **Validate your git environment** at the start of a session:

  ```bash
  bash scripts/check-git-env.sh
  ```

  This checks that `git` resolves to Git for Windows (not Cygwin/MSYS2) and
  that no stale `.git/index.lock` is blocking commits.

- **Avoid Cygwin `git`.** If `git` resolves to a Cygwin or non-MinGW MSYS path
  (e.g. `/cygdrive/...` or `/usr/bin/git`), it can produce signal errors and
  path-translation issues. Fix it by either:
  - adding `C:\Program Files\Git\cmd` to the **front** of your `PATH`, or
  - invoking `C:\Program Files\Git\cmd\git.exe` explicitly.

- **PowerShell command chaining:** do **not** chain with `&&` on Windows
  PowerShell 5.1 (it is not supported there). PowerShell 7+ does support `&&`,
  but to keep examples portable across both shells, chain with `;` and a
  success check instead. A non-terminating pattern suitable for an interactive
  shell:

  ```powershell
  npm run typecheck; if ($LASTEXITCODE -eq 0) { npm run build }
  ```

  Automated scripts that should abort on the first failure can use
  `exit $LASTEXITCODE` (see the pattern in `AGENTS.md`), but avoid `exit` in
  interactive shells — it closes the terminal window.

- **Stale `index.lock`:** if a commit fails with an `index.lock` error, first
  check for active `git` processes before deleting the lock.
  `scripts/check-git-env.sh` automates this check.

### macOS / Linux

No special setup. Use Bash/Zsh as you normally would. Standard `&&` chaining is
fine outside PowerShell.

---

## Default URLs

| Service | URL |
|---------|-----|
| Frontend | `http://localhost:5173` |
| Backend API | `http://localhost:5000` |
| Swagger | `http://localhost:5000/swagger` |
| Docker reverse proxy | `http://localhost:8080` |

---

## Running Tests

All commands assume you are in the repo root unless noted otherwise.
Treat these as the recommended local equivalents / minimum gates to keep
green before pushing. CI uses a few variants (e.g. `npm ci` instead of
`npm install`, `npm run test:coverage` with coverage thresholds instead of
plain `vitest --run`) — see `.github/workflows/` for the exact CI gate. If
you want to mirror CI's frontend gate precisely, run `npm run test:coverage`
locally as well.

### Backend tests (xUnit)

```bash
dotnet test backend/Taskdeck.sln -c Release -m:1
```

Run a single backend test class:

```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~MyTestClassName"
```

### Frontend checks

From `frontend/taskdeck-web`:

```bash
npm run lint                            # eslint
npm run typecheck                       # vue-tsc type checking
npx vite build                          # production build (matches CI)
npm run test:coverage                   # unit tests with coverage thresholds (matches CI)
npx vitest --run -t "test name"         # run a single test by name (dev-only)
```

`npm run build` also works locally — it runs typecheck followed by `vite build`
— but listing it alongside `npm run typecheck` would run typecheck twice, so
the list above mirrors CI (separate typecheck and `vite build` steps).

On Windows PowerShell, chain with `;` and a success check instead of `&&`
(see the Windows section above).

### End-to-end tests (Playwright)

From `frontend/taskdeck-web`:

```bash
npx playwright test --reporter=line
npx playwright test tests/e2e/some-spec.spec.ts   # run a single E2E file
```

For full test operations, fixtures, and troubleshooting, see
[docs/TESTING_GUIDE.md](docs/TESTING_GUIDE.md).

---

## Developer Certificate of Origin

Every commit must include a `Signed-off-by:` trailer certifying the
[Developer Certificate of Origin 1.1](https://developercertificate.org/).
Create the trailer from your configured Git name and email with `-s`:

```bash
git commit -s -m "Add booking validation to application layer"
```

The DCO is a lightweight statement that you created the contribution, or have
the right to submit it under Taskdeck's licence, and understand that the public
record is retained. Taskdeck uses this inbound-equals-outbound model instead of
a contributor licence agreement: contributions arrive under the same MIT terms
under which the core is distributed, so the
[MIT-forever commitment](LICENSING.md) does not depend on collecting unilateral
relicensing rights.

The CI check is advisory through the first rollout week (target: 2026-07-20).
Missing sign-offs still need to be corrected before the check is promoted into
branch protection under
[#1173](https://github.com/Chris0Jeky/Taskdeck/issues/1173). A sign-off is not a
cryptographic signature; do not add one on another contributor's behalf.

---

## Commit Message Conventions

- Use **present-tense, imperative** messages.
  Example: `Add booking validation to application layer`.
- Keep commits **small and focused** — avoid large mixed-topic commits.
- **One commit per file** is the default when a change spans multiple files,
  with a short file-specific description per commit.
- If a single logical change must touch multiple files, keep the smallest
  practical commit set and briefly explain why in the commit message body.
- **Exception:** pure file move/rename batches with no content changes may land
  as a single grouped commit — that is preferred.

Examples of good commit messages:

```
Add CONTRIBUTING.md with prerequisites and setup
Link CONTRIBUTING from README
Fix null reference in ReviewController.Approve
```

See `AGENTS.md` (Commit & Pull Request Guidelines) for the authoritative rules.

---

## Pull Request Process and Review Expectations

1. **Pick or file an issue first** for anything beyond a trivial fix. This
   keeps scope explicit and avoids wasted effort.
2. **Branch off `main`.** Name branches descriptively
   (e.g. `docs/doc-06-contributing-md`, `fix/review-null-ref`).
3. **Keep PRs scoped and small.** If you find a tangential refactor, open a
   follow-up PR rather than folding it in.
4. **Include verification evidence** in the PR description:
   - Commands run (tests, lint, typecheck) and their results.
   - Screenshots or short clips for user-visible UI changes.
5. **Link the issue** the PR closes or addresses (e.g. `Closes #873`).
6. **Wait for CI.** The required gate is `ci-required.yml`. PRs touching CI
   workflows (`.github/workflows/`), infrastructure (`deploy/`, `scripts/`),
   or project files (`*.csproj`) also trigger CI Extended — that must be green
   before merging those PRs.
7. **Self-review the diff** before requesting review. A deliberate
   reviewer-style pass on your own PR catches most avoidable feedback.

The full definition of done, required output format, and review protocol live
in [AGENTS.md](AGENTS.md). Read it before your first PR.

---

## Good First Issues

New to the repo? Start here:

- Browse [`good first issue`](https://github.com/Chris0Jeky/Taskdeck/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22)
  issues when any are open. These are scoped to be approachable without deep
  architecture context.
- Otherwise, look at open
  [`Priority III`](https://github.com/Chris0Jeky/Taskdeck/issues?q=is%3Aissue+is%3Aopen+label%3A%22Priority+III%22)
  issues — medium-priority work that is well-defined but not urgent, which
  makes it a good fit for a first contribution.
- Docs-labeled issues (e.g. `docs`) are also a low-risk entry point and help
  you learn the repo layout while contributing real value.

If you are unsure whether an issue is a good fit, comment on it and ask before
starting — we would rather redirect you early than have you burn time on
something that has shifted scope.

---

## Where to Go Next

- [AGENTS.md](AGENTS.md) — full contributor protocol and definition of done
- [README.md](README.md) — product overview and quickstart
- [docs/STATUS.md](docs/STATUS.md) — current shipped reality (read before non-trivial changes)
- [docs/IMPLEMENTATION_MASTERPLAN.md](docs/IMPLEMENTATION_MASTERPLAN.md) — delivery history and roadmap
- [docs/GOLDEN_PRINCIPLES.md](docs/GOLDEN_PRINCIPLES.md) — stable invariants
- [docs/TESTING_GUIDE.md](docs/TESTING_GUIDE.md) — test operations reference
- [docs/START_HERE.md](docs/START_HERE.md) — guided first-15-minutes walkthrough
- [docs/decisions/INDEX.md](docs/decisions/INDEX.md) — architecture decision records

Thanks for contributing.
