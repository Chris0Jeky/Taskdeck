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
# Edit deploy/.env and set a strong TASKDECK_JWT_SECRET before starting.
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

- **PowerShell command chaining:** do **not** chain with `&&`. Use `;` and
  check `$LASTEXITCODE` when you need to stop on failure. Example:

  ```powershell
  npm run typecheck; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; npm run build
  ```

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
