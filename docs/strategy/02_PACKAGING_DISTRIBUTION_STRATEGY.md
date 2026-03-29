# Packaging and Distribution Strategy

**Date:** 2026-03-29
**Scope:** Single-executable packaging, installer creation, cross-platform distribution, and first-run experience
**Status:** Strategic planning document — not yet executed

---

## 1. Current State

Taskdeck currently requires manual setup of multiple components:

```
# Backend (.NET 8)
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj

# Frontend (Vue 3 + Vite)
cd frontend/taskdeck-web && npm install && npm run dev
```

Or via Docker Compose:
```
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build
```

**The problem:** This is acceptable for developers during evaluation, but it's a significant barrier to adoption. Every step a user must take before seeing value is a potential drop-off point. The current setup requires:
- .NET 8 SDK or Docker installed
- Node.js 24 for frontend dev
- Git to clone the repo
- Manual env configuration
- Running multiple processes

**The goal:** Download one thing, double-click, use Taskdeck.

---

## 2. Architecture Advantage: ASP.NET Core Can Serve Everything

The single most important technical insight for packaging:

**ASP.NET Core's Kestrel server can serve the Vue SPA as static files alongside the API.**

This means the entire application — backend API, frontend SPA, and SQLite database — can run as a single process:

```
[Kestrel HTTP Server]
  ├── /api/*     → ASP.NET Core controllers
  ├── /hubs/*    → SignalR hubs
  └── /*         → Vue SPA static files (index.html + assets)
```

This architecture is the foundation for every packaging approach below.

---

## 3. Packaging Options Compared

### 3.1 Option A: .NET Self-Contained Single-File Executable (Recommended for V1)

**How it works:**
1. Build Vue frontend: `npm run build` → produces `dist/` folder
2. Copy `dist/` into ASP.NET Core's `wwwroot/`
3. Configure ASP.NET Core to serve SPA static files with fallback routing
4. Publish: `dotnet publish --self-contained -r win-x64 -p:PublishSingleFile=true -p:PublishTrimmed=true -p:IncludeNativeLibrariesForSelfExtract=true`
5. Result: **single .exe file** (~40-50MB trimmed, ~60-80MB untrimmed) that runs everything

**Pros:**
- Single file, no dependencies (runtime is embedded)
- Native performance (no Electron overhead)
- Small-ish download (~40-50MB trimmed, ~60-80MB untrimmed)
- Cross-platform (produce for win-x64, linux-x64, osx-x64, osx-arm64)
- Opens a browser tab automatically on launch
- SQLite database created on first run in user's app data folder
- Can be distributed via GitHub Releases

**Cons:**
- No native window chrome (runs in user's browser)
- Separate build per platform
- No system tray icon without additional work
- Auto-update requires custom implementation

**Implementation effort:** 1-2 weeks
**User experience:** Download → double-click → browser opens → use Taskdeck

### 3.2 Option B: Electron Wrapper

**How it works:**
- Electron app wraps the Vue frontend in a Chromium window
- Backend .NET process runs as a sidecar (spawned by Electron)
- IPC between Electron and .NET for lifecycle management

**Pros:**
- Native-feeling window (title bar, system tray, window management)
- Auto-update via Electron's update framework (electron-updater)
- Can publish to Microsoft Store, Snap Store
- Rich OS integration (notifications, file associations, deep links)

**Cons:**
- Large download (~150-250MB with Chromium + .NET runtime)
- Memory-heavy (~200-400MB RAM at idle)
- Complex build pipeline (Electron + .NET cross-compilation)
- Two processes to manage (Electron + .NET backend)
- Slow startup (Chromium initialization)

**Implementation effort:** 3-5 weeks
**User experience:** Download installer → install → launch app → native window

### 3.3 Option C: Tauri 2.0 Wrapper

**How it works:**
- Tauri uses the OS's native webview (Edge WebView2 on Windows, WebKit on macOS/Linux)
- Vue frontend runs in the webview
- .NET backend runs as a sidecar process (Tauri supports sidecar binaries)

**Pros:**
- Much smaller than Electron (~10-30MB for the Tauri shell + .NET sidecar)
- Native webview = less memory than Chromium
- Tauri 2.0 has mature sidecar support
- Auto-update built-in
- Can target mobile (Tauri 2.0 supports iOS/Android)
- Rust-based security advantages

**Cons:**
- Webview behavior varies across platforms (Edge vs WebKit vs WebKitGTK)
- Newer ecosystem, smaller community than Electron
- Debugging cross-platform webview issues can be harder
- Still requires .NET sidecar (~60MB) plus Tauri shell
- Total download: ~70-100MB

**Implementation effort:** 3-4 weeks
**User experience:** Download installer → install → launch app → native window (lightweight)

### 3.4 Option D: Docker Desktop (Current Approach, Enhanced)

**How it works:**
- Ship a one-command Docker Compose setup
- Possibly via Docker Extensions marketplace

**Pros:**
- Already works today
- Familiar to developers
- Identical environment everywhere
- Easy updates (docker pull)

**Cons:**
- Requires Docker Desktop installed (heavy dependency)
- Not suitable for non-developer users
- Docker Desktop licensing ($5/user/month for businesses)
- Extra layer of complexity for debugging
- No native OS integration

**Implementation effort:** Already done (minor polish needed)
**User experience:** Install Docker → run command → open browser

### 3.5 Comparison Matrix

| Criterion | .NET Self-Contained | Electron | Tauri 2.0 | Docker |
|-----------|-------------------|----------|-----------|--------|
| Download size | ~60-80MB | ~150-250MB | ~70-100MB | ~200MB+ |
| Memory usage | ~80-150MB | ~200-400MB | ~100-200MB | ~300-500MB |
| Startup time | 1-3 seconds | 3-8 seconds | 2-4 seconds | 10-30 seconds |
| Install friction | Very low (single file) | Low (installer) | Low (installer) | High (Docker req.) |
| Native feel | Browser tab | Full native | Native window | Browser tab |
| Auto-update | Custom needed | Built-in | Built-in | docker pull |
| Mobile potential | No | No | Yes (Tauri 2.0) | No |
| Implementation effort | 1-2 weeks | 3-5 weeks | 3-4 weeks | Done |
| Solo dev maintainability | High | Medium | Medium | High |

---

## 4. Recommended Approach: Phased Packaging

### Phase 1: Self-Contained Executable (Ship in 1-2 weeks)

**This is the minimum viable package.** It gets the "download and run" experience with the least effort.

Implementation steps:
1. Add SPA static file serving to ASP.NET Core startup
2. Add build script that: `npm run build` → copy to `wwwroot/` → `dotnet publish --self-contained`
3. Set up GitHub Actions to build for win-x64, linux-x64, osx-x64, osx-arm64
4. Publish as GitHub Releases with checksums
5. Add auto-launch browser on startup
6. SQLite DB auto-creates in `%APPDATA%/Taskdeck/` (Windows) or `~/.local/share/taskdeck/` (Linux/macOS)

**What the user sees:**
1. Go to GitHub Releases
2. Download `taskdeck-win-x64.zip` (or .tar.gz for Linux/macOS)
3. Unzip, double-click `Taskdeck.exe`
4. Browser opens to `http://localhost:5000`
5. Register, start using

### Phase 2: Platform Installers (Month 2-3)

Add proper installers for discoverability and polish:

| Platform | Installer | Distribution |
|----------|-----------|--------------|
| Windows | Inno Setup or MSIX | GitHub Releases, winget |
| macOS | .dmg with .app bundle | GitHub Releases, Homebrew cask |
| Linux | AppImage + .deb + .rpm | GitHub Releases, Snap Store, Flathub |

**Linux packaging shortcut:** [PupNet Deploy](https://github.com/kuiperzone/PupNet-Deploy) (`dotnet tool install -g KuiperZone.PupNet`) — purpose-built for .NET apps, generates AppImage, deb, and rpm from a single command and config file.

### Phase 3: Native Desktop Shell (Month 4-6, if justified by demand)

If users ask for native window experience, system tray, or native notifications:
- **Tauri 2.0 wrapper** around the self-contained backend
- Adds: native window, system tray, global hotkey, auto-update
- Bonus: opens the path to mobile (Tauri 2.0 mobile targets)

### Phase 4: Package Manager Distribution (Month 6+)

| Manager | Command | Audience |
|---------|---------|----------|
| winget | `winget install Taskdeck` | Windows developers |
| Homebrew | `brew install --cask taskdeck` | macOS developers |
| Snap | `snap install taskdeck` | Linux (Ubuntu) |
| Flatpak | `flatpak install taskdeck` | Linux (general) |
| Chocolatey | `choco install taskdeck` | Windows (sysadmin) |
| npm (global) | `npx taskdeck` | Node.js developers |
| Docker Hub | `docker run taskdeck` | Container-native users |

---

## 5. First-Run Experience Design

The packaging isn't just about installation — it's about the first 60 seconds.

### 5.1 First-Run Flow

```
User launches Taskdeck
  ↓
Browser opens to http://localhost:5000
  ↓
Welcome screen: "Welcome to Taskdeck"
  - "Create your account" (local auth, data stays on device)
  - Takes: display name + password (email optional)
  ↓
Guided first-board creation
  - "What kind of work do you track?" → starter pack selection
  - Or "Start blank" for power users
  ↓
Quick capture tutorial
  - "Try capturing something right now" → pre-filled example
  - Hotkey hint: Ctrl+Shift+C for quick capture
  ↓
Show the result: proposal generated, review and apply
  ↓
Board updated — "You're ready to go!"
```

### 5.2 Time-to-Value Targets

| Step | Target Time | Current Reality |
|------|-------------|-----------------|
| Download to running | <60 seconds | 5-15 minutes (manual setup) |
| Running to registered | <30 seconds | ~30 seconds (acceptable) |
| Registered to first capture | <30 seconds | ~30 seconds (acceptable) |
| First capture to proposal | <30 seconds | Depends on LLM provider |
| Total: download to first value | <3 minutes | 15-30 minutes |

### 5.3 Default Configuration

The packaged app should work with zero configuration:
- Mock LLM provider by default (instant, deterministic, no API keys needed)
- SQLite database auto-created in platform-appropriate location
- JWT secret auto-generated on first run
- Default port: 5000 (with auto-fallback if occupied)
- Optional: "Connect your own AI" setup in Settings for live LLM providers

---

## 6. Auto-Update Strategy

### 6.1 Phase 1: Manual Updates (Release Downloads)

- GitHub Releases with version-tagged downloads
- In-app notification: "A new version is available" with download link
- Check for updates on launch (opt-in, privacy-respecting)

### 6.2 Phase 2: Semi-Automatic Updates (Velopack)

**Recommended framework: [Velopack](https://velopack.io/)** — successor to Squirrel.Windows, cross-platform (Windows/macOS/Linux), delta updates that apply in ~2 seconds, no UAC prompt. Install via NuGet: `Velopack`. Handles download, delta patching, and restart seamlessly. Preferred over rolling a custom update mechanism.

- Check version endpoint: `GET /api/health/version` returns current + latest
- Download and apply in-app (requires restart)
- Differential updates if download size is a concern

### 6.3 Phase 3: Automatic Updates (Tauri/Electron)

- Background download of updates
- Apply on next restart
- Rollback capability
- Update channels: stable, beta, nightly

---

## 7. Code Signing and Security

### 7.1 Why It Matters

Unsigned executables trigger security warnings:
- Windows SmartScreen: "Windows protected your PC"
- macOS Gatekeeper: "cannot be opened because the developer cannot be verified"
- These warnings kill conversion. Many users won't click through.

### 7.2 Cost and Effort

| Platform | Certificate Type | Cost | Effort |
|----------|-----------------|------|--------|
| Windows | EV code signing cert | $200-400/year (or free via SignPath for OSS) | Medium |
| macOS | Apple Developer cert | $99/year (Apple Developer Program) | Medium |
| Linux | GPG signing | Free | Low |

### 7.3 Recommendation

- Start without code signing (accept the warnings for early beta)
- Add Windows/macOS signing once you have 100+ users
- Apply for SignPath OSS program (free EV signing for open source)
- Apple Developer Program is worth the $99/year if you have macOS users

---

## 8. Build and Release Pipeline

### 8.1 CI/CD Additions Needed

```yaml
# New GitHub Actions workflow: release-desktop.yml
on:
  push:
    tags: ['v*']

jobs:
  build-frontend:
    # npm ci && npm run build → artifact

  build-desktop:
    needs: build-frontend
    strategy:
      matrix:
        include:
          - os: windows-latest
            rid: win-x64
            artifact: taskdeck-win-x64.zip
          - os: ubuntu-latest
            rid: linux-x64
            artifact: taskdeck-linux-x64.tar.gz
          - os: macos-latest
            rid: osx-x64
            artifact: taskdeck-osx-x64.tar.gz
          - os: macos-latest
            rid: osx-arm64
            artifact: taskdeck-osx-arm64.tar.gz
    steps:
      - dotnet publish --self-contained -r ${{ matrix.rid }}
        -p:PublishSingleFile=true
        -p:PublishTrimmed=true
        -p:IncludeNativeLibrariesForSelfExtract=true
        -c Release
      - Upload release artifacts

  create-release:
    needs: build-desktop
    # Create GitHub Release with all platform artifacts + checksums
```

### 8.2 Versioning Strategy

Use semantic versioning aligned with release milestones:

| Version | Meaning |
|---------|---------|
| 0.1.0 | First packaged beta (self-contained executable) |
| 0.2.0 | Platform installers |
| 0.3.0 | Cloud/hosted option |
| 0.4.0 | Mobile PWA |
| 0.5.0 | Native desktop shell (Tauri) |
| 1.0.0 | First stable release (all platforms, polished UX) |

---

## 9. Size Optimization

### 9.1 .NET Self-Contained Size Reduction

| Technique | Size Impact | Complexity |
|-----------|-------------|------------|
| `PublishTrimmed=true` | -30-50% | Medium (requires trim compatibility testing) |
| `PublishReadyToRun=true` | +10-20% but faster startup | Low |
| Compression (zip/tar.gz) | -40-60% | Trivial |
| NativeAOT (ahead-of-time) | -60-70% + faster startup | High (not all .NET features supported) |

**Estimated final sizes:**

| Platform | Uncompressed | Compressed |
|----------|-------------|------------|
| win-x64 | ~70MB | ~30-40MB |
| linux-x64 | ~65MB | ~25-35MB |
| osx-x64 | ~65MB | ~25-35MB |
| osx-arm64 | ~60MB | ~25-30MB |

These are well within acceptable ranges for 2026 desktop applications (Obsidian is ~180MB, VS Code is ~300MB, Notion is ~400MB).

### 9.2 Frontend Build Size

The current Vite build should produce:
- ~1-3MB of JS (gzipped: ~300-800KB)
- ~200-500KB of CSS
- Static assets (fonts, icons)
- Total: ~2-5MB uncompressed

---

## 10. Implementation Priority

| Priority | Task | Effort | Impact |
|----------|------|--------|--------|
| **P0** | Fix P0 blockers (#508, #509) before any packaging work | 1-2 days | Critical |
| **P1** | Add SPA static file serving to ASP.NET Core | 2-3 hours | Foundation |
| **P1** | Build script (frontend build + dotnet publish) | 4-6 hours | Foundation |
| **P1** | GitHub Actions release workflow | 4-8 hours | Distribution |
| **P1** | First-run auto-config (JWT, DB path, browser launch) | 4-6 hours | UX |
| **P2** | Windows Inno Setup installer | 4-8 hours | Polish |
| **P2** | macOS .app bundle + DMG | 4-8 hours | Polish |
| **P2** | Linux AppImage | 2-4 hours | Polish |
| **P2** | Update check mechanism | 4-8 hours | Retention |
| **P3** | Tauri 2.0 wrapper | 2-3 weeks | Native feel |
| **P3** | Package manager submissions | 1-2 days each | Discovery |
| **P4** | Code signing (Windows + macOS) | 1-2 days | Trust |
| **P4** | NativeAOT investigation | 1-2 weeks | Performance |

---

## Related Documents

- `deploy/docker-compose.yml` — Current Docker deployment
- `deploy/docker/backend.Dockerfile` — Backend container build
- `deploy/docker/frontend.Dockerfile` — Frontend container build
- `.github/workflows/ci-release.yml` — Existing release CI
- `docs/strategy/00_MASTER_STRATEGY.md` — Master strategy document
