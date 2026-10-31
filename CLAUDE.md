# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A WPF desktop app (Windows only) for creating, converting, validating, installing, and sharing themes for Lian Li L-Connect 3 LCD devices (HydroShift II LCD S/C, HydroShift II OLED Curve, 8.8" Universal Screen, 9.2" VM). It also hosts an optional embedded ASP.NET Core "Phone Link" server and an in-app theme gallery.

## Commands

```powershell
dotnet build .\ThemeEditorCSharp.csproj -c Release     # also builds SupporterCs first
dotnet run --project .\ThemeEditorCSharp.csproj

dotnet tool restore                                     # restores WiX 7 from dotnet-tools.json
.\build-msi.ps1                                         # -> bin_build\installer\LianLiThemeEditorSetup.msi

dotnet build .\ThemeEditorCSharp.csproj -p:SkipSupporterBuild=true   # skip the net48 worker build
dotnet build .\SupporterCs\SupporterCs.csproj                        # worker only

dotnet test .\Tests\ThemeEditor.Tests\ThemeEditor.Tests.csproj       # xUnit v3; skips the worker build
```

Build output goes to `bin_build/` (`BaseOutputPath`), not `bin/`.

### Tests

`Tests/ThemeEditor.Tests` covers the services outside MainWindow and the `lang/*.json` files. CI (`.github/workflows/tests.yml`) runs it on Windows for pushes and PRs that touch more than `templates/` or Markdown. MainWindow itself is still untested; `docs/refactor/mainwindow-split-plan.md` is the plan for changing that.

- The test project lives inside the root project's folder. The root csproj excludes `Tests\**`, so keep new test code under `Tests\`.
- App types are reachable through `InternalsVisibleTo("ThemeEditor.Tests")`, so make a helper `internal`, never `public`, when you extract it for testing.
- Service tests are characterization tests: they pin today's behaviour. A test commented `// Current behaviour; looks wrong` marks a suspected bug. Fix the code and flip the assertion in the same commit.
- `LanguageFileTests.MissingKeyBaseline` caps untranslated keys per locale. Adding an English key without translating it fails the test; lower the numbers when translations land.
- No test may touch the network, the registry, ProgramData/LocalAppData or a running L-Connect.

There is no linter config. Anything the tests don't cover still needs manual verification: build, run, and exercise the affected tab. Most device workflows (import/export/apply) need L-Connect 3 installed and running; gallery browsing and package validation work without it.

Public releases ship the WiX MSI only. Old releases used Inno Setup, which is why `installer-wix/Product.wxs` still looks up the Inno `_is1` uninstall key for upgrades.

## Two-project architecture

**`ThemeEditorCSharp.csproj`** (`net10.0-windows`, assembly `LianLiThemeEditor`) — the WPF app.

**`SupporterCs/SupporterCs.csproj`** (`net48`, assembly **`UsbMonitorL`**) — a console worker, copied to output as `LianLiThemeEditor.TemplateWorker.exe` by its PostBuild target.

The `net48` split and the `UsbMonitorL` assembly name are both load-bearing:

- L-Connect `.turtheme`/`.template` payloads are **BinaryFormatter** graphs whose type names live in assembly `UsbMonitorL`. `SupporterCs/TurzxModels.cs` declares matching `[Serializable]` types in `namespace UsbMonitorL` so deserialization binds. Renaming the assembly or moving those types breaks every import/export path.
- The worker reflects into L-Connect's own `lianli.ThemeEngine.dll` (`Assembly.LoadFrom` + an `AssemblyResolve` hook, `SupporterCs/Program.cs` ~line 7878) for real rendering. BinaryFormatter is unavailable on modern .NET, hence `net48`.

`Services/SupporterBridge.cs` is the only thing that talks to the worker: it spawns the exe per call with `-Flag value` arguments (`-ListFonts`, `-ListGraphStyles -Json`, layer-apply arg sets, plus positional `test` / `fix-turzx` subcommands), serialized behind a `SemaphoreSlim`. Worker discovery order: `LIANLI_THEME_SUPPORTER` env var → app dir → `SupporterCs/bin/{Debug,Release}/net48/`. `SupporterBridge.WorkingDirectory` is also what locates `lang/`.

## MainWindow is enormous — never read it whole

`MainWindow.xaml.cs` is ~37,900 lines (1.5 MB) and `MainWindow.xaml` ~5,600 lines. There are no `#region` markers. Always `Grep` for a symbol, event handler, or UI element name and read a narrow window around the hit. Reading the file (or asking a subagent to) will blow out context.

The window is one `partial class MainWindow` holding nearly all editor workflow: layer editing, preview rendering, undo/redo (`_undoStack` of `EditSnapshot`, pushed via `PushUndoState(...)`), autosave/recovery timers, import/convert/export, gallery, diagnostics. The MVVM types under `ViewModels/` are supporting scaffolding — `DataContext = this` on the window itself.

Tabs are `x:Name`d in `MainWindow.xaml`: `EditorTab`, `GalleryTab`, `LocalThemesTab`, `ConvertFixTab`, `PhoneLinkTab`, `SettingsTab`, `DiagnosticsTab`, `ThanksTab`, `AboutTab`. That naming is the fastest way to find the code for a feature.

## Services

`Services/ThemeEditorServices.cs` is a hand-rolled composition root: `CreateDefault()` builds every service and `MainWindow`'s internal constructor unpacks them into fields. The parameterless `MainWindow()` chains to it. To swap an implementation, go through the interfaces (`ISupporterBridge`, `IGalleryManifestService`, …) and `CreateDefault()`.

## L-Connect integration

- **Install location** — `Services/LConnectPaths.cs` resolves it via `LIANLI_LCONNECT_DIR` → uninstall registry keys → `LConnectService`/`LConnectServiceWatcher` service `ImagePath` → `%ProgramFiles%\Lian-Li\L-Connect 3`. Never hardcode the path.
- **Control API** — `Services/LConnectClientService.cs` talks to the local L-Connect service over HTTP at `127.0.0.1:11021` (with port discovery fallback), and probes two request shapes (`Legacy` and `OfficialCompatible`) because L-Connect's protocol differs across builds. The working port+mode is cached.
- **ffmpeg** — video/H.264 background work shells out to L-Connect's bundled `x64\ffmpeg.exe`; the app does not ship its own.
- **Misspelled engine members** — `Services/ThemeEngineNames.cs` pins ThemeEngine field names that are typos upstream (`totalAngel`, `ration`). Keep using the constants; do not "fix" the spelling.
- `IntegratedPhoneControl/LConnectClient.cs` is a second copy of `LConnectClientService`'s protocol logic under `namespace PhoneControl` (it predates the Phone Link merge into this assembly). Fixes to L-Connect protocol behavior usually need to be applied in both places. Install-path resolution is shared: PhoneControl files alias `ThemeEditorCSharp.Services.LConnectPaths`.

## Gallery: this repo is the CDN

`templates/` is not just build input — it is the live gallery source. `Services/GalleryManifestService.cs` fetches `templates/gallery.json` from `raw.githubusercontent.com/ozgurce/LianliThemeEditor/main` (and the GitHub contents API), and packages/previews are served from `templates/packages/` and `templates/previews/`. Recent commit history is almost entirely gallery publishing ("Publish N approved theme(s)", "Remove package: …") done by tooling, not source changes.

Consequences when touching `templates/`:

- Do not delete or bulk-rewrite `templates/gallery.json`, `templates/packages/**`, or `templates/previews/**` as part of a source change. Remote-only gallery assets can exist that a local workspace does not have.
- `.gitignore` ignores `*.zip` globally, so gallery `.zip` packages are force-added; a new package needs `git add -f`.
- `templates/blank-seeds/*.template` are different — those ship with the editor and are copied to output.

Submissions go to a Cloudflare Worker (`https://lianli-theme-gallery.ozgurce.workers.dev`) via `GallerySubmissionService`; approved submissions are then published into this repo, and the Worker copy is removed so GitHub stays the single public source.

## Theme package formats

- **Editor package** (`.lltheme` / `.zip`) — ZIP with `manifest.json` matching `Models/ThemePackageManifest.cs` (`FormatVersion` 1, `DeviceModel`, `TemplateId`, `TemplateFile`, `BackgroundFile`, `PreviewFile`, `UniversalOrientation`, `ImageFiles`, `FontFiles`). The model also carries lowercase legacy aliases (`id`, `deviceModel`, `template`, …) and the validator accepts several key casings — keep both readable when changing it.
- **Raw L-Connect ZIP** — no manifest, just a `.template` plus media. `ThemePackageValidationService` detects and handles this shape separately.
- **`.turtheme`** — BinaryFormatter graph; only the worker can read/write it.
- `FontFiles` are for **gallery** packages only; normal exports must not include fonts, or L-Connect rejects them.

`Services/ThemePackageValidationService.cs` enforces zip-slip protection and a 500 MB per-entry cap — preserve those checks.

## Device models and orientation

Canonical ids are `hydroshift-ii-lcd-s`, `hydroshift-ii-lcd-c`, `universal-screen-8.8-inch`, `vm-9.2-inch`, `hydroshift-ii-oled-curve`. Route any externally-sourced value through `Services/GalleryDeviceModelNormalizer.Normalize`.

Universal Screen 8.8" is orientation-sensitive (`1920x480` landscape / `480x1920` portrait) and is the source of most historical bugs. Established rule from CHANGELOG: infer orientation from explicit user selection, then stored template orientation, then embedded template/canvas dimensions — **never** from file names, and never treat a rotated `.h264` runtime background's dimensions as proof of portrait.

## Localization

`lang/{en,tr,de,fr,ko,ru,zh}.json` are nested JSON flattened to dotted keys. `LoadLanguage(lang)` loads `en` first and overlays the selected language, so English is always the fallback; files are read from `_supporter.WorkingDirectory/lang`. In code, strings come from `GetLanguageText("key.path", "English fallback")` — always pass a real fallback. New user-facing strings should be added to `lang/en.json` at minimum.

## Local state (useful when debugging)

- `%LOCALAPPDATA%\LianLiThemeEditor\Logs\editor-yyyyMMdd.log` — `Services/AppLogger.cs`
- `%LOCALAPPDATA%\LianLiThemeEditor\recovery.json` — autosave snapshot, `Services/RecoveryService.cs`
- `%LOCALAPPDATA%\LianLiThemeEditor\Fonts` — staged/installed theme fonts
- `%PROGRAMDATA%\Lian-Li\L-Connect 3` — L-Connect's own theme/profile data
- Phone Link settings live in `PhoneLinkWeb/appsettings.json` next to the app, written by `PhoneControlSettingsStore`

## Releasing

The version lives in `ThemeEditorCSharp.csproj` in six places (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`, `Title`, `AssemblyTitle`) and `build-msi.ps1` reads `Version` from the csproj. A release bump also means a new `## V x.y.z` section at the top of `CHANGELOG.md` and updating the "Current Version" section of `README.md` — README currently lags at 2.6.5 while the csproj/CHANGELOG are at 2.8.7, so don't trust README for the version.

The in-app updater looks for an MSI asset on GitHub releases and runs it through `msiexec`; releases must not ship a setup EXE.


<!-- BEGIN BEADS INTEGRATION v:1 profile:minimal hash:6cd5cc61 -->
## Beads Issue Tracker

This project uses **bd (beads)** for issue tracking. Run `bd prime` to see full workflow context and commands.

### Quick Reference

```bash
bd ready              # Find available work
bd show <id>          # View issue details
bd update <id> --claim  # Claim work
bd close <id>         # Complete work
```

### Rules

- Use `bd` for ALL task tracking — do NOT use TodoWrite, TaskCreate, or markdown TODO lists
- Run `bd prime` for detailed command reference and session close protocol
- Use `bd remember` for persistent knowledge — do NOT use MEMORY.md files

**Architecture in one line:** issues live in a local Dolt DB; sync uses `refs/dolt/data` on your git remote; `.beads/issues.jsonl` is a passive export. See https://github.com/gastownhall/beads/blob/main/docs/SYNC_CONCEPTS.md for details and anti-patterns.

## Agent Context Profiles

The managed Beads block is task-tracking guidance, not permission to override repository, user, or orchestrator instructions.

- **Conservative (default)**: Use `bd` for task tracking. Do not run git commits, git pushes, or Dolt remote sync unless explicitly asked. At handoff, report changed files, validation, and suggested next commands.
- **Minimal**: Keep tool instruction files as pointers to `bd prime`; use the same conservative git policy unless active instructions say otherwise.
- **Team-maintainer**: Only when the repository explicitly opts in, agents may close beads, run quality gates, commit, and push as part of session close. A current "do not commit" or "do not push" instruction still wins.

## Session Completion

This protocol applies when ending a Beads implementation workflow. It is subordinate to explicit user, repository, and orchestrator instructions.

1. **File issues for remaining work** - Create beads for anything that needs follow-up
2. **Run quality gates** (if code changed) - Tests, linters, builds
3. **Update issue status** - Close finished work, update in-progress items
4. **Handle git/sync by active profile**:
   ```bash
   # Conservative/minimal/default: report status and proposed commands; wait for approval.
   git status

   # Team-maintainer opt-in only, unless current instructions forbid it:
   git pull --rebase
   git push
   git status
   ```
5. **Hand off** - Summarize changes, validation, issue status, and any blocked sync/commit/push step

**Critical rules:**
- Explicit user or orchestrator instructions override this Beads block.
- Do not commit or push without clear authority from the active profile or the current user request.
- If a required sync or push is blocked, stop and report the exact command and error.
<!-- END BEADS INTEGRATION -->
