# Public release/source mirror: options, recommendation and mechanism

Last Updated: 2026-09-05 · Decision: ADR-0066 SC-8 (2026-09-03) · Issue: CI-16 #2439 · Human gate: CI-13 #2337 section A

The repository goes private for the v0.3.0 release. The SC-8 ruling recorded in
[PRIVATE_REPO_CUTOVER_CHECKLIST.md](PRIVATE_REPO_CUTOVER_CHECKLIST.md) section A says
"Pages keeps publishing; Releases, checksums/provenance and the GPL source publish through the
mirror (CI-16 #2439); launch-kit and `awesome-selfhosted` wording point at the mirror." This memo
picks the mirror's shape and the mechanism a later workflow PR implements. Creating the mirror is
human-only (below).

## The three options

- **(a) Snapshot per release tag.** A separate public repository whose `main` receives one commit
  per release: the tree of the released tag, produced from `git archive`, plus the Release assets
  published against a matching tag in the mirror.
- **(b) Whole history on every tag.** The same repository, but every release pushes the private
  repository's full commit history, not a snapshot.
- **(c) Releases only.** The mirror carries no source tree; each Release attaches a source tarball
  alongside the binaries.

## Scoring

| Criterion | (a) Snapshot per tag | (b) Whole history | (c) Releases only |
| --- | --- | --- | --- |
| ADR-0050 corresponding source. LICENSING.md: "Modified distributions of the GPL-covered Taskdeck core must remain under GPLv3 and provide corresponding source as the licence requires." | Satisfied. Every released binary has a browsable, cloneable public tree at the exact released state, reachable without downloading anything. | Satisfied, and then some. History is not part of the corresponding-source obligation, so the extra is unrequired. | Satisfied. A tarball attached to the Release is a direct GPLv3 offer, but a reader must download and unpack it to read or diff the source. |
| Checksum/provenance chain. `release-desktop.yml` generates `<archive>.sha256` per artifact (step "Generate SHA256 checksum"), re-verifies with `sha256sum -c ./*.sha256` (step "Verify checksums") and writes `taskdeck-<tag>-provenance.txt` naming the tag, commit, repository and run before publishing. | Unchanged. The mirror re-publishes the identical asset bytes and the identical `.sha256` files, and compares before and after upload; the provenance asset still names the private run that built the bytes. | Same as (a). The history push does not touch assets. | Same as (a) for the binaries, plus one new asset (the source tarball) whose sha256 the mirror generates itself rather than copying, so one link of the chain starts in the mirror job instead of the build job. |
| `awesome-selfhosted` activity criteria. LAUNCH_KIT.md records that the criteria "require a first release more than four months old, active maintenance, and working installation instructions". | Mechanically as strong as the private repository: a public Release cadence, a public tree that changes on every release, and installation instructions in the mirrored README. Eligibility is a separate clock: LAUNCH_KIT.md's `awesome-selfhosted` section records the project "withheld; not eligible for submission", first release 2026-08-19, so the release-age criterion is not met before roughly 2026-12-19, and a new mirror's visible release history starts empty until the open question 5 backfill runs. | Same, with a denser public commit graph. | Weakest. A repository with no source tree shows release activity but no code activity, and the installation instructions would have to be duplicated into the mirror README by hand. |
| Private-repository secret boundary. RUNNER_TOPOLOGY_AND_THREAT_MODEL.md sets the posture for CI credentials: "No static PAT on the runner; no release/signing/cloud secrets". The mirror must carry no token, no runner label and no private workflow, and must run no Actions of its own. | Good. One reviewable tree per release, cut by a default-deny export list and secret-scanned before push. The mirror never receives refs from the private repository, so no private branch, no unreleased work and no dropped commit can travel. | Worst. Every historical blob becomes permanently public, including any secret ever committed and every workflow file with its `runs-on` labels, and a public git history cannot be unpublished once cloned. It also contradicts the point of SC-8, which is that development goes private. | Best in isolation. Nothing is pushed, so nothing can leak through a tree. The tarball still needs the same scan, so the advantage is smaller than it looks. |

## Recommendation

**Option (a), snapshot per release tag.** It alone satisfies the corresponding-source obligation in
a form a reader can browse and diff, keeps the public activity signal the launch kit depends on,
and still exports a small, filtered, individually reviewable surface. Option (b) is rejected
because publishing the whole history is irreversible and defeats the ruling that development goes
private. Option (c) is rejected because it makes the source the hardest artifact to reach in a
project whose licence is its selling point, and is a weak public identity.

What (a) costs:

- **No history rewriting or filtering of the private repository.** The snapshot comes from
  `git archive <tag>`, not from a ref push, so no private history is exported and nothing has to be
  scrubbed retroactively. The mirror grows one commit per release.
- **A secret scan of the snapshot before every push.** The repository already runs a full-history
  Gitleaks scan on tag push with `fail-on-findings: true` (`ci-release.yml`, `scan-mode: full`), but
  the snapshot has no history, so the mirror job needs a filesystem scan of the extracted tree with
  the same `.gitleaks.toml`, failing closed (step 4).
- **A default-deny export list, not a `.github` strip.** Dropping only `.github/workflows/**` and
  `.github/actions/**` would still publish the control-plane and agent surface SC-8 sends private:
  `.claude/`, `.codex/`, `.gemini/`, `.agent-harness/`, `.paper-issues/`, `.semgrep/`, `ci/`,
  `autodoc/`, `scripts/{ci,agent_hooks,agentic}/`, `docs/{ci,agentic,analysis}/` and the root agent
  files (`AGENTS.md`, `CLAUDE.md`, `GEMINI.md`, `OUTSTANDING_TASKS.md`, `.mcp.json`, `mcp*.json`).
  The proposal is therefore an export allowlist shipped with the workflow as
  `ci/mirror-export.txt`, everything unlisted dropped. Keep: `backend/`, `frontend/`, `extensions/`,
  `ee/`, `deploy/`, `demo/`, `tests/`, `scripts/` minus those three subtrees, the root build inputs
  (`global.json`, `.nvmrc`, `.gitattributes`, `.gitignore`, `.dockerignore`, `.env.example`), the
  licence set (`LICENSE`, `LICENSES/`, `LICENSING.md`, `RELICENSING.md`, `SECURITY.md`), the reader
  docs (`README.md`, `UPGRADING.md`, `docs/USER_MANUAL.md`, `docs/START_HERE.md`,
  `docs/MCP_SERVER.md`, `docs/api/`, `docs/platform/{CONFIGURATION_REFERENCE,LLM_PROVIDER_SETUP_GUIDE}.md`,
  `docs/releases/notes/<tag>.md`), plus the generated `.github/README.md` pointer. Default-allow
  with a strip list is rejected: a control-plane directory added later would publish itself, while
  an allowlist fails closed (open question 3).
- **A licence cost inside that list.** Stripping `ci/` and `scripts/ci/` removes build-control files
  a strict reading of the GPLv3 "scripts used to control compilation and installation" clause could
  claim as corresponding source; they drive CI verification, not building or installing, which
  `scripts/` (kept), `deploy/` and the manifests do. Open question 9, default strip.
- **The Pages site source does not move.** Checklist A records that `pages-frontend.yml` keeps
  publishing from the private repository on Pro and the site stays public. The mirror hosts no Pages
  site; only the links inside the site and the launch kit change, in the follow-up PR.
- **A backfill decision for past releases** (open question 5) and one extra hosted job per tag.

## Publishing mechanism for option (a)

A single workflow in the **private** repository, triggered by the release the existing pipeline
already published, snapshots the tag, verifies the bytes, then pushes and re-publishes. The mirror
runs nothing; it is a destination. A later PR implements it as `.github/workflows/mirror-release.yml`.

1. Trigger on `release: types: [published]` plus `workflow_dispatch` with a `dry-run` input that
   defaults to `true`. The release trigger is already used by `release-security.yml`, and it can
   never run ahead of the assets the way a raw tag push can.
2. Check out the released tag with `fetch-depth: 1` and `persist-credentials: false`.
3. Build the snapshot: `git archive` the tag into a clean directory, keep only the paths in
   `ci/mirror-export.txt` and delete the rest, write the pointer `.github/README.md`, then assert
   that no path from the deny classes above survived.
4. Scan the snapshot directory with the pinned Gitleaks CLI and the repository's `.gitleaks.toml`,
   redacted output, non-zero exit on any finding. A snapshot-wide grep for `self-hosted` or
   `runs-on:` cannot be the second check because it is not satisfiable: measured on this branch, 244
   such occurrences sit in 85 tracked files outside `.github` (`docs/analysis/`, `scripts/ci/`,
   `ci/schemas/`, ADR-0066 and more), and 9 in 7 files survive even the export list above
   (`README.md`, `docs/platform/CONFIGURATION_REFERENCE.md`, product source), every one the word
   `self-hosted` in deployment prose. So the check is split.
   - **Fails the run:** any surviving file under a `.github/` directory at any depth other than the
     generated pointer (the tree carries a nested one under
     `docs/archive/2026-02-25_inreview-repo-pack/REPO_PACK/`); any `.yml`/`.yaml` file parsing to a
     mapping with a `runs-on:` key at any level; any `secrets.` expression inside a `${{ }}` block;
     any `ghp_`, `gho_`, `ghs_` or `github_pat_` token shape.
   - **Reported, never fails:** the count and file list of literal `self-hosted` and `runs-on:`
     strings elsewhere in the snapshot, in the evidence artifact so a reviewer sees drift.
5. Download the private Release assets with `gh release download <tag>`, including every `.sha256`
   file and `taskdeck-<tag>-provenance.txt`.
6. Run `sha256sum -c ./*.sha256` over the downloaded set. Any mismatch or missing file fails the
   run before anything public is written. This is the same assertion `release-desktop.yml` makes at
   its "Verify checksums" step, re-made against the bytes actually being re-published.
7. Create the mirror Release for `<tag>` **as a draft**, which creates no tag and is invisible to
   anonymous readers: the private Release's composed body (`gh release view --json body`, download
   links rewritten to the mirror), the same prerelease flag, the exact downloaded asset files,
   their `.sha256` files, the provenance asset, and `taskdeck-<tag>-source.tar.gz` with its `.sha256`.
8. Re-download the draft's assets and compare their sha256 against the private `.sha256` files a
   second time. On any mismatch, delete the draft and fail. Nothing public exists at this point:
   no commit, no tag, no visible Release.
9. Only now publish. Commit the snapshot into the mirror checkout as one commit on `main`
   ("Taskdeck `<tag>` source snapshot (`<sha>`)"), tag it `<tag>`, push both with the mirror
   credential (that commit and tag only; never a private ref), then flip the draft to published
   against `<tag>`. Pushing last is the rollback story: a public commit and tag cannot be retracted
   once anyone has cloned them, while every earlier step is a local file or a deletable draft, so a
   checksum mismatch leaves nothing public at all.
10. Credential: one fine-grained personal access token in the **private** repository's secrets,
    named `MIRROR_PUBLISH_TOKEN`, scoped to the single mirror repository, permission
    `Contents: Read and write` and nothing else, no organization resources, with an expiry and a
    rotation date. A write deploy key can replace the push half but cannot call the Releases API,
    so the token is still needed for steps 7 to 9.
11. The mirror repository has Actions disabled in its settings, so no pushed content can execute
    there even if a workflow file ever slipped through step 3.

## Rehearsal while still public

Checklist I already lists a "release dry-run (no publish)" as a rehearsal item. This is that item
for the mirror, run on a prerelease tag (for example `v0.3.0-rc.1`) with `dry-run: true`:

- It checks out the prerelease tag and performs steps 3 through 6 exactly as the real path does:
  the same export list, the same Gitleaks scan, the same failing and reporting checks, the same
  asset download and the same `sha256sum -c`. It then runs `git push --dry-run` against the mirror
  to prove the credential resolves and the ref update is the expected one, and stops. It calls no
  mutating `gh` command and uploads nothing.
- **Ordering constraint.** The push half needs the mirror repository and `MIRROR_PUBLISH_TOKEN`,
  both human-only (open questions 1 and 7), so the maintainer must create them before checklist
  section I completes, and section I precedes the SC-6 visibility flip.
- **Fallback without them.** Run the same job with the push step skipped: steps 3 through 6
  unchanged, `git push --dry-run` recorded in the manifest as `skipped: mirror repository or
  credential not provisioned`, exit 0. That proves the filter, the scan and the checksum chain and
  leaves only the credential and ref assertions for the section I item to still owe.
- Evidence artifact `mirror-rehearsal-<tag>`, retention 90 days, recorded on CI-13 #2337: the
  snapshot file list with a sha256 per file, the Gitleaks JSON report, both step 4 outputs, the
  full `sha256sum -c` output, the `git push --dry-run` output or its skip reason, the rendered
  mirror release body, and a manifest naming the private run id and the resolved commit.
- Pass condition: zero Gitleaks findings, zero hits on any of the four failing checks in step 4,
  every checksum line `OK`, no mutating call in the log, and either the dry-run push reporting the
  expected ref with no error or the recorded skip reason. The reported `self-hosted` and `runs-on:`
  listing is read by a human and never decides the pass.

## Human-only (restated from #2439)

- "Creating the mirror repository and any token/deploy key for it (kept out of the repository), and
  confirming its name — recorded on #2337 (B2 settings evidence)."
- "The visibility flip itself stays SC-6."

## Open questions for the maintainer

1. **Mirror repository name.** Default proposal: `Chris0Jeky/taskdeck-release`, the private
   repository keeping its name. The alternative, renaming the private repository and giving the
   mirror the `Taskdeck` name so existing public links keep resolving, is a larger move that would
   have to precede the pointer edits.
2. **Confirm option (a)** over (b) and (c). Default: (a), as recommended above.
3. **Snapshot export list.** Default: the default-deny allowlist above (`ci/mirror-export.txt`),
   with the deny classes asserted absent, a pointer `.github/README.md`, and Actions disabled in
   the mirror settings as well.
4. **Source tarball on every Release in addition to the pushed tree.** Default: yes, it makes the
   corresponding-source offer self-contained per Release for a cheap extra asset.
5. **Backfill of v0.1.0 through v0.2.x** into the mirror as snapshot commits and releases, so old
   download links have a public home. Default: yes, once, dry-run first.
6. **Issues and Discussions on the mirror.** Default: both off, with the mirror README pointing at
   the security policy route; the launch kit's public issue-tracker link then needs a replacement
   destination in the follow-up PR.
7. **Credential shape and rotation.** Default: `MIRROR_PUBLISH_TOKEN` as specified in step 10, with
   a 90-day expiry and a rotation reminder on #2337.
8. **GHCR image visibility.** `release-container.yml` publishes to GHCR, and package visibility is
   configured separately from repository visibility. Default: treat it as out of scope for this
   memo and raise it on #2337 so the images do not silently go private with the repository.
9. **Build-control files and ADR-0050 corresponding source.** Whether `ci/` and `scripts/ci/` count
   as GPLv3 "scripts used to control compilation and installation" and must therefore ship in the
   snapshot. Default: no, strip them, on the reading above. This memo does not decide the licence
   question; if they are kept, they enter `ci/mirror-export.txt` and step 4's checks tighten.
