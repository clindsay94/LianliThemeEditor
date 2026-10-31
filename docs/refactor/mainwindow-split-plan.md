# MainWindow split + test plan

Status: proposal, 2026-10-31. Supersedes the scope of bead `LianliThemeEditor-2oz`.
Numbers come from a brace-aware parse of `MainWindow.xaml.cs` at `0e4e9f5`.

## TL;DR

1. **Phase 0, test harness.** Add `Tests/ThemeEditor.Tests` and test the services that already
   exist outside MainWindow. Nothing in MainWindow moves yet.
2. **Phase 1, mechanical split.** A re-runnable Roslyn script moves members verbatim into about 18
   `partial` files and edits no method bodies. Because it's a script, it can be re-run on top of
   in-flight branches instead of hand-merging 38k lines.
3. **Phase 2, extract static helpers.** 536 static methods (9.7k lines) touch no UI. They move into
   `internal static` classes and get characterization tests. `using static` means call sites don't change.
4. **Phase 3, stateful services where the bugs are.** `LayerEditSession` goes first. 38 methods
   (1.8k lines) own the pending-queue and undo state behind six open P0/P1 beads, and the
   extraction becomes the vehicle for `-47f`.
5. **Phase 4, MVVM only where it pays.** The layer property panel and the gallery tab. The other 650
   `x:Name` controls stay in code-behind.

Don't start Phase 1 until the three branches that touch MainWindow have landed or been parked (see Prerequisites).

## What we're dealing with

| Metric | Value |
|---|---|
| `MainWindow.xaml.cs` | 37,911 lines, one class, 0 `#region`s |
| Members | 1,303 methods, 208 fields, 18 properties, 23 nested types |
| Static methods | 544 (9,904 lines), of which 536 touch no `x:Name` control |
| Static, no UI, no IO signal | 392 methods / 5,304 lines, testable as-is once made `internal` |
| Event handlers | 219 in code; XAML wires 307 events to 187 handlers |
| Async | 222 async methods, 67 `async void` |
| `.Result` / `.Wait()` / `GetResult()` | 9 sites |
| Empty `catch {}` | 10 sites |
| Biggest methods | `ImportThemePackageAsync` 567, `PopulateEditorFromSelection` 544, `ApplyLanguage` 522, `CreateGraphPreview` 290 |
| Method size spread | 741 under 20 lines; 52 between 100 and 300; 3 over 300 |
| XAML | 5,643 lines, 672 `x:Name`, 116 `{Binding}` |
| `ViewModels/` | 284 lines; `DataContext = this` (`MainWindow.xaml.cs:529`) |

**Coupling is lower than the size suggests.** Of 226 fields and properties, 109 are used by only one
feature area and 62 by two. A few hubs carry almost all the cross-feature traffic: `_isLoading`
(13 areas), `_supporter` (11), `Layers` (11), `_currentTemplatePath`/`_currentTemplateId` (9),
the device-model constants (8–9) and `_currentBackgroundPath` (8). The split works because of this:
most fields move with their feature, and the hubs become a small `EditorState`.

### Feature areas (by method name, first-match heuristic)

| Area | Methods | Lines | Static (lines) | Touch UI | Handlers |
|---|---:|---:|---:|---:|---:|
| Layers (grid, selection, add/move/delete, grouping) | 177 | 4,437 | 36 (466) | 74 | 46 |
| Rendering and preview (canvas, GDI text, D3, OLED curve) | 157 | 4,431 | 59 (1,251) | 45 | 31 |
| L-Connect integration (activation, profiles, refresh) | 101 | 4,171 | 60 (1,714) | 6 | 2 |
| Theme package IO (import/export/convert/Turzx) | 96 | 3,805 | 40 (826) | 18 | 19 |
| Unassigned, needs manual mapping | 200 | 3,559 | 121 (1,644) | 38 | 30 |
| Gallery | 118 | 3,267 | 47 (815) | 20 | 14 |
| Templates, themes, orientation, device | 106 | 2,847 | 37 (457) | 38 | 14 |
| Background and video (ffmpeg) | 64 | 2,757 | 27 (762) | 17 | 6 |
| Sensors and data sources | 99 | 2,564 | 54 (806) | 18 | 7 |
| Localization | 33 | 1,296 | 6 (81) | 11 | 2 |
| Fonts | 55 | 1,176 | 39 (822) | 8 | 5 |
| Input wiring (combo/slider helpers) | 51 | 630 | 10 (169) | 41 | 32 |
| Settings, self-update, tray/window, phone link | 46 | 1,170 | 8 | 10 | 11 |

L-Connect integration touches UI in only 6 of its 101 methods. It's already a service in all but name.

## Prerequisites

1. **Branches touching MainWindow.** Land or park these before Phase 1:
   - `fix/lcd-font-scope`: 1 commit, +355/−19 in MainWindow (bead `-51r`).
   - `claude/app-optimization-review-494b5a` worktree: +142/−16, **uncommitted**, and that session is still open.
   - `sync/upstream-2.9.1`: 43 commits with **no merge base** with `main`.
2. **Upstream.** `main` and `upstream/main` share no history, so upstream sync is already a manual
   port. After the split, porting an upstream fix means finding the method by name (token-savior
   `find_symbol`) instead of by line. Upstream changes MainWindow often (4 commits since our base),
   so expect a few ports per month.
3. **Open bug beads.** `-1r3` and `-7yp` were fixed in `0e4e9f5`, but they're still open pending an
   end-to-end repro. Bead `-2oz` said to split only after the P0/P1 fixes land. That still holds for
   hand fixes, but the scripted split in Phase 1 relaxes it, and `-47f` is better done *as* Phase 3.

## Phase 0: test harness (1 PR, small)

- **Project:** `Tests/ThemeEditor.Tests/ThemeEditor.Tests.csproj`, `net10.0-windows`, `UseWPF`,
  xUnit v3. Add `Xunit.StaFact` for the few tests that need an STA thread (`DrawingContext`,
  `BitmapSource`). Check xUnit v3 compatibility when adding it. If it isn't compatible, MSTest's
  built-in `[STATestMethod]` is the fallback.
- **Root csproj gotcha:** `ThemeEditorCSharp.csproj` sits at the repo root and globs everything below it.
  `<Compile Remove="Tests\**\*.cs" />` already exists. Also add `Tests\**` to `DefaultItemExcludes`
  so the test project's `obj/` and `bin/` don't leak in as `None` items.
- **Reference:** `ProjectReference` to the WinExe, which works in .NET, with
  `AdditionalProperties="SkipSupporterBuild=true"` so tests don't rebuild the net48 worker.
- **`InternalsVisibleTo("ThemeEditor.Tests")`** in `AssemblyInfo.cs`. Extracted helpers become
  `internal`, not `public`.
- **First tests**, on code that already lives outside MainWindow, to prove the harness:
  `ThemePackageManifestNormalizer`, `GalleryDeviceModelNormalizer`, `GalleryFilterService`,
  `GalleryPaginationService`, `LayerGroupService`, `ThemePackageValidationService` with small zip
  fixtures built in the test, and `FastProfileReader`.
- **Cheap high-value test:** every key in `lang/en.json` exists in each other `lang/*.json`, and no
  locale has orphan keys. This catches drift like `-bm3`.
- **CI:** a GitHub Actions `windows-latest` job running `dotnet test`. There's no `.github/` today.

## Phase 1: mechanical partial-class split (1 commit, medium)

**Why a script, not hand edits:** three branches are in flight, and upstream ports keep arriving. A
script driven by a mapping file can be re-run on any version of the file, so a conflict becomes "re-run
the splitter on their version" rather than a 38k-line merge.

- `tools/MainWindowSplitter/`, a small Roslyn console app. `tools\**` is already excluded from the
  app's compile. Its input is `mainwindow-split.map` (member name → target file). It moves each
  member verbatim with its leading trivia, writes `MainWindow.<Area>.cs` files that each declare
  `public partial class MainWindow` with the original usings, and fails if any member is unmapped.
- **Self-check:** the script hashes (signature, body text) for every member before and after and
  fails on any difference. That's the real guarantee of "no behaviour change". Comparing DLL bytes
  won't work, because member order changes the metadata order.
- XAML event handlers and `x:Name` fields keep working from any partial. They're members of the same class.
- Add the move commit to `.git-blame-ignore-revs`. `git blame -C -C` follows the moved lines.

**Proposed files.** Target ≤ 4k lines each. The 200 unassigned methods get assigned while reviewing the map.

| File | Contents |
|---|---|
| `MainWindow.xaml.cs` | ctor, hub fields, `Loaded`/`Closing`, `SetBusy`, top-level wiring |
| `MainWindow.Native.cs` | P/Invoke structs and imports, `WindowMessageHook`, tray icon, monitor info |
| `MainWindow.Layers.cs` | layer grid, selection, add/remove/duplicate/reorder, grouping |
| `MainWindow.LayerHistory.cs` | pending queues, dirty tracking, `Apply*`, undo/redo, autosave, recovery |
| `MainWindow.LayerEditor.cs` | `PopulateEditorFromSelection`, `UpdateLayerFromInputs`, `RegisterInputListeners`, `OnInputChanged` |
| `MainWindow.Preview.cs` | `DrawPreview`, drag/resize/nudge, zoom, guides |
| `MainWindow.Preview.Text.cs` | GDI text render and measure (`GetGdiTextLayerRender` and similar) |
| `MainWindow.Preview.Graphs.cs` | graph/gauge/clock/arc/D3 preview |
| `MainWindow.Sensors.cs` | live sensor cache, hardware model values, data-source status |
| `MainWindow.Templates.cs` | template list, local themes, backups, orientation, device selection |
| `MainWindow.Background.cs` | background media, video, ffmpeg conversion |
| `MainWindow.ImportExport.cs` | package import/export, convert/fix, Turzx, zip helpers |
| `MainWindow.LConnect.cs` | activation (88/92/OLED), profiles, refresh triggers, device paths, service control |
| `MainWindow.Gallery.cs` | browse, filter, download/install, submit, votes |
| `MainWindow.Fonts.cs` | font discovery, registry install, private GDI fonts |
| `MainWindow.Localization.cs` | `ApplyLanguage`, mapped-language text |
| `MainWindow.Settings.cs` | editor settings load/save, first-run defaults |
| `MainWindow.Update.cs` | self-update check/download/dialog |
| `MainWindow.PhoneLink.cs` | phone link server and menu |

## Phase 2: extract static helpers (several small PRs, one area each)

**Technique (no call-site edits):** move `private static Foo(...)` into
`internal static class OrientationInference` (or similar) and change it to `internal`. Then add
`using static ThemeEditorCSharp.Core.OrientationInference;` to the partials that call it. The
compiler flags any ambiguity.

**Tests are characterization tests.** Assert what the code does today, odd cases included, not what
we think it should do. Fix behaviour in a separate commit once a test pins it.

High-value first targets:

| Helper(s) | Line | Why |
|---|---:|---|
| `GetSafeZipEntryPath`, `IsUnsafePackageEntryName`, `GetSafeFileName` | 18778, 20632 | Two different zip-slip guards. Pin both, then consolidate. |
| `CreateLayerDeleteSignature`, `GetLayerDeleteMatchScore`, `GetLayerDeleteSortKey` | 6078, 6101, 6254 | Delete matching is in the P0 bug path |
| `ResolvePendingLayerMoveSourceIndex`, `TryParseNestedLayerIndex`, `TryGetNestedLayerZIndex` | 10532, 3694, 6926 | Same |
| `InferUniversalOrientationFrom*`, `InferWide*OrientationFromLayers`, `InferGalleryThemeOrientation` | 16365, 18271, 23207, 23452, 25281, 23945 | 7+ overlapping heuristics. Upstream keeps patching these. |
| `IsRemoteVersionNewer`, `TryParseVersionNumber` | 22173, 22196 | A self-update decision. A wrong answer means a bad update or no update. |
| `NormalizeUniversal88DefaultCustomTheme(Configs)`, `Set/ClearUniversal88ConfigBackgroundPath` | 33773, 34003 | Rewrites L-Connect config. JSON in, JSON out. |
| Gallery keys: `GetGalleryThemeMergeKey`, `ExtractGalleryThemeStableToken`, `GetGalleryDownloadKey` | 22647, 22678, 22258 | Dedup and merge correctness |
| `ReadExifOrientation`, `ApplyExifOrientation` | 37631, 37591 | Image fixtures |
| `TryParseLayerSourceRect`, `NormalizeClockRate`, `GetClockLayerAngle`, `GetGraphDataRatio` | 12079, 8327, 8158, 11514 | Preview math |
| `SampleValueFor`, `NormalizeReleaseNotesForDisplay`, `IsEditorSettingsVersionCurrent` | 12357, 9870, 30667 | Quick wins |

**The 144 static helpers that do IO (4.4k lines):**
- File IO: pass paths in and test against a temp directory. Don't introduce `IFileSystem` everywhere.
- HTTP: the static `SharedHttpClient` becomes an injected `HttpClient`, so tests can supply an
  `HttpMessageHandler`.
- Processes (ffmpeg, `sc.exe`, L-Connect): add a small `IProcessRunner` only where a test needs one.

**Global mutable state:** 18 static fields hold the font maps, sensor caches and `PrivateFontCollection`.
Turn them into instance classes owned by MainWindow (`FontCatalog`, `LiveSensorCache`) so each test
gets a fresh instance.

## Phase 3: stateful services where the bugs are (one PR each)

**3a. `LayerEditSession`, the highest-ROI item in this plan.**
- **Owns:** `_pendingLayerMoves`, `_pendingLayerDuplicates`, `_pendingLayerDeletes`,
  `_pendingDeleteSourceIndexesByEditorId`, `_dirtyLayers`, `_pendingDirtyLayersAfterAdd`, the undo
  and redo stacks, and the `EditSnapshot`/`PendingLayer*` records, which become `internal` top-level types.
- **Scope:** 38 methods (1,784 lines) touch this state today, including `ApplyAllButton_Click`,
  `ApplyDirtyLayersAsync`, `Apply*PendingAsync`, `UndoAsync`/`RedoAsync`, `PushUndoState`,
  `AutoSaveTimer_Tick` and `ApplyTemplateResult`.
- **Design:** a plain C# class with no WPF dependency that takes `ISupporterBridge`. MainWindow
  delegates to it and keeps only the UI refresh.
- **Bugs it covers:** `-7yp`, `-0z6`, `-41i`, `-8sw`, `-vsz` and the `-47f` redesign. Write each bug
  as a failing test against the session, then implement `PendingLayerAdd` keyed on `EditorLayerId`
  (the `-47f` design) until they pass.

**3b. `EditorState` and a loading guard.** Gather the hub fields into one object. Replace the
`_isLoading` counter (17 sites, `-1r3`) with an `IDisposable` scope so an exception can't leave it stuck.

**3c. Then, in order of size and risk:**

| Service | Main sources | Notes |
|---|---|---|
| `LConnectActivationService` | `ActivateUniversal88ThemeAsync` (222), `ActivateVm92ThemeAsync` (168), `Trigger(Fast)LConnectRefreshAsync` (159, 149), `GetLConnectDevicePaths` (132) | `ILConnectClientService` already exists to mock. Almost no UI. |
| `ThemePackageImporter` / `ThemePackageExporter` | `ImportThemePackageAsync` (567), `ExportLConnectButton_Click` (230), `ConvertUniversal88LConnectZipToVm92Async` (142) | Split the 567-line import into pipeline steps, with zip fixtures for tests |
| `BackgroundMediaService` | `ConvertExportBackgroundAsync` (219), `ResolveBackgroundPath` (199), ffmpeg calls | Fixes `-c2o` (blocking 60 s transcode, swallowed errors, orphaned process) with an async process plus cancellation |
| `GalleryController` | 118 remaining gallery methods | Five gallery services already exist. This removes the remaining glue. |
| `UpdateService` | `StartSelfUpdateAsync`, `ShowUpdateAvailableDialog` | |
| `LocalizationService` | `ApplyLanguage` (522), `ApplyMappedLanguageText` (162) | Mostly control→key tables. Make them data, then test key coverage. |

## Phase 4: MVVM, selectively

Don't convert 672 `x:Name`s to bindings for its own sake. It pays in two places:

- **Layer property panel.** `PopulateEditorFromSelection` (544), `UpdateLayerFromInputs` (217),
  `RegisterInputListeners` (134) and `OnInputChanged` are about 1k lines of manual control↔model
  sync. A `LayerEditorViewModel` bound two-way to `LayerRow` removes most of that and makes edits
  testable without a window.
- **Gallery tab.** `GalleryViewModel` already exists. Grow it, and route `GalleryController` through it.

Everything else can stay code-behind. A thin code-behind over tested services is fine.

## Out of scope

- A rewrite, or migrating to an MVVM framework (CommunityToolkit, Prism).
- A coverage-percentage target. Test the bug-dense and decision-making code.
- Body edits in the Phase 1 move commit.
- UI automation (FlaUI). It would be brittle until `-0o1` adds `AutomationProperties`. At most, one
  launch smoke test later.
- `SupporterCs/Program.cs` (7,950 lines) and `IntegratedPhoneControl/LConnectControlService.cs`
  (3,151) have the same problem, but each is a separate project. They're a follow-up.

## Sequencing

| Step | Size | Depends on | Unblocks |
|---|---|---|---|
| 0 Harness + existing-service tests + lang parity + CI | S | — | everything |
| 1 Splitter script + map review + move commit | M | branches landed or parked | 2, 3 |
| 2 Static extraction, one area per PR (≈10 PRs) | S each | 1 | 3 |
| 3a `LayerEditSession` + bug tests + `-47f` | L | 1, 2 (layer helpers) | closes 5 P0/P1 beads |
| 3b `EditorState` + loading guard | S | 1 | 3c |
| 3c Services table, top to bottom | M each | 2 | 4 |
| 4 Layer editor VM, gallery VM | M | 3a, 3c gallery | — |

Phases 0 and 2 parallelize well: each area's extraction is independent once Phase 1 has landed.
