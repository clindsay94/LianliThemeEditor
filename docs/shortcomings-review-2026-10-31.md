# Theme Editor shortcomings review (2026-10-31)

MW = `MainWindow.xaml.cs`. "Confirmed" means the code path was traced end to end and spot-checked; "suspected" means plausible but not fully traced.

## Bottom line

- **Reverts** have one root mechanism: every reload (`ApplyTemplateResult`, MW:2881) silently clears `_dirtyLayers` and all pending move/duplicate/delete queues. Any action that writes to disk and reloads without first flushing unapplied edits throws them away. Only the Add-layer handlers protect against this.
- **Rotation** comes from the orientation dropdown being view-only. Reload trusts the template's `isLanscape` flag first (MW:2983), but nothing in the edit path writes that flag except `SetBackgroundRefs`. Pick Portrait, make any structural edit, and the reload flips it back.
- **Missing gauges (RAM frequency, timings)** are an L-Connect limit, not an editor bug. L-Connect's renderer only reads a fixed key set, and RAM frequency/timings aren't in it, although HWiNFO (which L-Connect bundles) does collect them. The honest fix is a "hardware spec" static-text inserter, not a live gauge.
- **Slowness** mostly comes from launching a new .NET Framework worker process for *every* template operation, then reloading the whole template several times per Apply.
- **Structure:** `MainWindow.xaml.cs` is 37,910 lines and ~1,100 methods, with no tests. That is why fixes like 0e4e9f5 keep being needed: there is no single owner for editor state.

## 1. Edits reverting

| # | Status | Trigger | Where | Fix direction |
|---|---|---|---|---|
| 1 | Confirmed, high | Edit a property, don't Apply, then Move Up/Down or Bring Forward/Send Backward | `MoveSelectedLayerAsync` MW:4621, `MoveSelectedLayersOneStepAsync` MW:4672 | `ForceSyncPendingStructuralChangesAsync` only flushes structural queues, and the menu variant flushes nothing. Flush dirty layers first, or use the capture/restore pair from the Add handlers (MW:8813/8826). Skip `RefreshIfTemplateStructureChangedAsync` while anything is pending. |
| 2 | Confirmed, high | Edit layers, then change background; or edit *during* the up-to-120 s background job | `ChooseAndSetBackgroundAsync` MW:4961, reload MW:5109 | Flush before starting; `SetBusy` (MW:34860) should disable the property editors. |
| 3 | Confirmed, medium | Queue a delete/drag or edit, then Duplicate group | `DuplicateLayerGroupAsync` MW:9419 | Never flushes, and duplicates by UI index against un-rebased disk indexes, so it can copy the wrong layer. |
| 4 | Confirmed, timing | Keep editing while Apply All "saves in background" | `SupporterBridge.cs:324`, MW:5724-5729 | The batch is serialized at call start, then those layers are marked clean anyway. Use a per-layer version and only clear dirty if unchanged. |
| 5 | Confirmed | Ctrl+Z in a text box, or undo after Apply/template switch | MW:30430, MW:35177 | Ctrl+Z is caught at window level (undoes the whole layer, not the typo). One snapshot per selection. `_undoStack` is never cleared on load, so undo can restore another template's layers. |
| 6 | Suspected | Apply All after L-Connect touches the file; any edit on OLED Curve | MW:5722 vs 5478; MW:2910, 3530-3540 | The external-change check gives false positives, so it reloads and drops edits. Compare by `SourceLayerIndex`, skip synthetic rows, re-read the timestamp after the L-Connect refresh. |
| 7 | Suspected | "Use active template" mode while L-Connect switches theme | `ResolveTemplateTargetAsync` MW:3369-3384 | Edits are written by index into whatever is active now. Refuse or prompt when it differs from the loaded template. |
| 8 | Suspected | Type immediately after selecting a layer | MW:3631, MW:35173, MW:5434 | Input is ignored until a ContextIdle callback clears a flag; Apply only reads inputs for already-dirty layers. Use the hydration version token. |
| 9 | Confirmed, low | Restore a recovery snapshot | MW:35522, 35556, 3784 | The snapshot holds only dirty layers but restore replaces the whole list; the file isn't cleared on most apply paths and lingers 7 days. |

**Structural fix:** a single `CommitPendingEditsAsync()` gate that every disk-writing action calls first, plus a rule that `ApplyTemplateResult` never discards dirty state silently (merge it, or refuse to reload).

## 2. Theme rotating

Orientation has about 8 sources of truth: the template flag, canvas size, background media size, theme name words, media references, `TemplateOption`, the combo, and layer extents. Each call site ranks them differently. On reload the flag always wins (MW:2983), so the other seven are dead code there.

| # | Status | Trigger | Where |
|---|---|---|---|
| 1 | Confirmed | Change the orientation combo, then any structural edit/Apply All | Combo handler MW:14593 only changes the canvas and editor settings, never the template. |
| 2 | Confirmed (editor side) | Apply Orientation, then a structural edit | MW:14680 calls `SetBackgroundMediaAsync` without a landscape arg; `SetBackground` (Program.cs:1641) writes width/height but not `isLanscape`. Masked by forcing the combo right after load (MW:14706/14749). |
| 3 | Confirmed | New Theme in the non-default orientation | MW:1415-1460: the seed keeps its own flag/size; only the UI is set. |
| 4 | Suspected | Gallery install | MW:19197-19203, 37423/37456: preview dimensions override `manifest.UniversalOrientation`; the preserved-background branch (19215) skips writing the flag. |
| 5 | Suspected | Restart | MW:6369 writes canvas size from the combo without the flag. |

**Fix:** one owner. A `SetOrientation` worker command that writes `isLanscape` plus width/height together, called on every path that changes orientation; reload reads only that; inference only for templates that genuinely lack the flag.

## 3. Telemetry gauges

L-Connect 3 (`C:\Program Files\Lian-Li\L-Connect 3`) renders on the PC in `L-Connect-Service` using `lianli.ThemeEngine.dll`, fed by its bundled HWiNFO. The renderer only reads a fixed key dictionary. The editor's `DataSources` list (MW:209) already covers every key L-Connect actually feeds.

| Metric | In HWiNFO data? | Possible on device? |
|---|---|---|
| RAM frequency, timings (Tcas/Trcd/Trp/Tras/CR) | Yes | No live key. Static text only. |
| RAM DIMM temp, motherboard/VRM temps | Yes | No |
| CPU CCD/hotspot temp, per-core clock | Yes | Partly: rebind CPUTEMP/CPUCLOCK in L-Connect (one at a time) |
| GPU memory junction temp, memory clock | Yes | No |
| Per-header fan RPM | Yes | One via CPUFAN rebind; CASEFAN1-8 only on Lancool 207 |
| Per-adapter network, disk read/write | Yes | No |
| FPS 1%/0.1% lows | Yes (PresentMon) | No |

Editor-side problems found:
- CASEFAN3-8 are shown as "Limited" (MW:29987) but render blank on Universal/HydroShift; gauges and bars accept only CASEFAN1/2.
- FPS_AVG reads 0 when L-Connect's `IsHWiNFOPresentMonEnabled` is off, and the editor gives no warning.
- Preview diverges from device: CPULOAD uses "Total CPU Usage" (MW:12593) instead of L-Connect's "Total CPU Utility"; the user's `SensorMapping` is ignored; UPSPEED/DOWNDSPEED preview divides by 100 against KB/s (MW:8340), so gauges sit pinned at full.

Recommended:
1. "Insert hardware spec" action: reads `C:\ProgramData\Lian-Li\L-Connect 3\hwinfo-sensors.json` (it does contain Memory Clock and Memory Timings) and writes StaticText such as "DDR5-6000 CL30-38-38-96". It goes stale only after an XMP/tuning change.
2. In-app help explaining L-Connect sensor rebinding for CCD temp, per-core clock or a specific fan.
3. Fix the CASEFAN, FPS_AVG and preview-scaling issues above.
4. Not recommended: pushing images through L-Connect's undocumented local API (port 11021), or patching L-Connect.

## 4. Performance

Ranked by impact over effort:

1. **A new worker process per template operation** (`SupporterBridge.cs:911-1000`, `Process.Start` at :936). Each call pays .NET Framework startup, `Assembly.LoadFrom` of L-Connect's DLL (Program.cs:7879), a full BinaryFormatter round-trip, and up to 4 retries. → Long-lived worker over stdin/stdout or a named pipe, with the theme kept loaded.
2. **Moving a layer N slots launches N processes** (MW:10483-10497, 4713ff). → One reorder op.
3. **Apply reloads the whole template several times** (`ApplyButton_Click` MW:4343, plus MW:10504 and ~6031; 31 `LoadTemplatePathAsync` call sites). → Write ops return the updated layer JSON.
4. **Synchronous ffmpeg on the UI thread**: `LoadBackgroundPreview` (MW:26478) blocks on `WaitForExit(60000)` (MW:32768) and an untimed `WaitForExit()` (MW:27214); timed-out processes are never killed. → Async, kill on timeout, cache probes by path+mtime.
5. **Full preview rebuild every second**: `DrawPreview` (MW:6536) clears and recreates every visual; the live timer (MW:565) fires whenever a dynamic layer exists, even when the Editor tab is hidden. 28 direct `DrawPreview()` calls bypass the coalescer. → Update dynamic visuals in place; gate on tab visibility.
6. **Language switch re-parses ~30 JSON files, three times** (MW:28456, 28234/28253, 27799/27826/27845). → Parse once, cache.
7. **Unbounded gallery byte cache** of 29-46 MB zips (MW:469, 23094, 24285). → Size-capped LRU or disk cache.
8. **20 `new HttpClient`** in MW despite `SharedHttpClient` (MW:81); 11 more in `LConnectControlService.cs:3149`.
9. **`Thread.Sleep` retries** in async flows (MW:6497, 33156, 33481, 36919).
10. **GDI text cache keyed on x/y** (MW:13149), so dragging re-renders text every pixel, and the cache is wiped at 1000 entries.
11. **Startup/packaging**: `build-msi.ps1:15` publishes framework-dependent with no RID and no ReadyToRun. → `-r win-x64 -p:PublishReadyToRun=true`.

## 5. Code health

- **God class**: MW has 37,910 lines, ~208 fields, 73 `Task.Run`, 274 catch blocks. It mixes UI, ffmpeg, HTTP, service control, GDI rendering and file I/O. MVVM is a facade: `ViewModels/` is 284 lines total, and MainWindow.xaml has 629 `x:Name` vs 104 `{Binding}`.
- **No tests.** The csproj excludes `Tests\**` but no test project exists.
- **Swallowed exceptions**: 87 empty or comment-only catches in MW, 33 in SupporterCs, 10 in LConnectControlService, 5 in FastProfileReader.
- **Mojibake**: `SupporterBridge.cs:1331-1335` holds repair `.Replace` chains whose search strings are themselves multiply mis-encoded, so they can never match. Fallback labels at MW:28986/29013 etc. show "Ãƒâ€šÃ‚Â°F" if language loading fails. MW:12943 has a dead `Contains("Ãƒâ€šÃ‚Â°")`.
- **Localization**: 83 keys used in code are missing from `en.json`. 61 keys are missing from de/fr/ko/ru/zh. About a third of German (329) and Korean (260) values are untranslated English.
- **Repo bloat**: ~1.5 GB of gallery packages under `templates/` tracked directly in git, without LFS.
- **Duplication**: `Services/LConnectPaths.cs` and `IntegratedPhoneControl/LConnectPaths.cs` are identical apart from one `using`.
- **Clean**: the main project builds with 0 warnings and nullable enabled.

## Suggested order

1. **Stop data loss**: the commit-pending-edits gate (§1 #1-3) plus orientation single-owner (§2). Small, high-value, fixes both headline complaints.
2. **Undo correctness** (§1 #5) and the Apply All version race (§1 #4).
3. **Persistent worker** (§4 #1-3): the biggest speed win, and it removes most reload paths that cause reverts.
4. **UI-thread ffmpeg and preview redraw** (§4 #4-5).
5. **Telemetry**: the hardware-spec inserter plus the CASEFAN/FPS/preview fixes.
6. **Ongoing**: add a test project around SupporterBridge and the worker protocol, then extract services out of MainWindow one at a time.
