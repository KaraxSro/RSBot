# RSBOT improvements — implementation and verification checklist

## Implementation status (2026-09-09)

The planned code changes in this document have been implemented. The unchecked matrices below remain the manual, in-game verification checklist; they are intentionally not marked complete by a compile-only verification.

Implementation choices for the formerly open logging decisions:

- session logs use `Build/User/Logs/Sessions/<date>`;
- retention is 10 MB per file with rollover and 30 days by age;
- historical log scrolling and selections are preserved silently, while an `Open log folder` action was added;
- recent Windows Application Error/WER lookup runs best-effort after an unexpected early client exit;
- crash-dump collection remains disabled and still requires explicit opt-in.
- Immortal, Astral, and Steady remain user-selectable when a matching stone is available. When selected, Immortal then Astral are applied before the +5 attempt, and Steady is applied before +6 and later attempts.

# Items → Pickup settings TODO

## Goal

Make the item-rule behavior unambiguous and keep the rule tester stable and immediately up to date.

Rare equipment and normal equipment must use separate rule systems:

- the **Rare items** tab exclusively controls recognized rare items;
- the **Equipment** tab controls only normal equipment;
- a rare item that is not selected for storage must never fall through to the normal equipment Degree/type/gender rules.

## 1. Separate rare and normal equipment storage logic

- [ ] Treat `ItemCategoryRules.GetRareRuleKey(item) != null` as a terminal classification.
- [ ] In `ItemCategoryRules.ShouldStore`, return the matching rare Store checkbox state directly for a recognized rare item.
- [ ] Do not evaluate normal Degree, equipment type, or gender rules for a recognized rare item.
- [ ] Apply normal Equipment-tab rules only when `GetRareRuleKey(item)` returns `null`.
- [ ] Preserve the existing explicit per-item `ShoppingManager.StoreFilter` override.
- [ ] Preserve the rule that Store wins if an item is explicitly present in both legacy Store and Sell filters.
- [ ] Keep supplies on their independent category-rule path.

Expected town behavior for rare equipment:

1. Explicit per-item Store filter → **Store**.
2. Matching Rare Store checkbox enabled → **Store**.
3. Rare Store checkbox disabled and explicit Sell filter enabled → **Sell**.
4. Rare Store checkbox disabled and `Sell equipment not selected for storage (including rare items)` enabled → **Sell**.
5. Otherwise → **Keep**.

Expected town behavior for normal equipment:

1. Explicit per-item Store filter → **Store**.
2. Matching Equipment Degree/type/gender rules → **Store**.
3. Explicit Sell filter or automatic selling of unselected equipment enabled → **Sell**.
4. Otherwise → **Keep**.

Suggested internal separation:

- [ ] Extract or clearly separate `ShouldStoreRareItem`.
- [ ] Extract or clearly separate `ShouldStoreNormalEquipment`.
- [ ] Extract or clearly separate `ShouldStoreSupply`.
- [ ] Consider a single evaluation result containing `Store`, `Sell`, or `Keep` plus the rule/reason that produced it. Use the same result in `ShoppingManager` and the UI tester.

## 2. Clarify the UI

- [ ] Add an explanation to the Rare items tab: `Rare equipment is controlled exclusively by this tab.`
- [ ] Change the Equipment tab explanation to say that Degree, type, and gender rules apply only to normal equipment.
- [ ] Optionally show the winning rule in the test result, for example:
  - `Store — Rare rule: Sun.7`
  - `Sell — Sun.7 is not selected for storage`
  - `Store — Normal equipment rule: Degree 7`

## 3. Stop the test textbox results from jumping while typing

Current likely cause: `TextUpdate` rebuilds the ComboBox item collection and closes/reopens the dropdown after every keystroke. It also resets the selected index and caret position.

- [ ] Add a short UI debounce using a WinForms timer; start with **250 ms**.
- [ ] Restart the timer on every text edit and refresh suggestions only after the user has stopped typing for the debounce interval.
- [ ] Do not query or mutate the ComboBox item collection on every keystroke.
- [ ] Update the dropdown only when the calculated suggestion set has actually changed.
- [ ] Preserve the typed text, caret position, and current scroll/selection where possible.
- [ ] Avoid forcibly closing and reopening the dropdown unless WinForms requires it.
- [ ] Cancel/dispose the pending timer when the control is disposed or the game-data source changes.
- [ ] Keep keyboard behavior predictable: arrows navigate suggestions, Enter tests the selected item, and Escape closes the dropdown.
- [ ] Verify that fast typing, deleting, pasting, and holding Backspace do not cause visible jumping or lost characters.

## 4. Fix the invisible mouse cursor in the test field

No global `Cursor.Hide()` call was found. The test ComboBox explicitly sets `Cursor = Cursors.Default`, so the local ComboBox/edit-control cursor behavior should be investigated first.

- [ ] Reproduce over each part of the tester: editable text area, dropdown button, suggestion rows, Test button, and result label.
- [ ] Remove the unnecessary explicit `Cursor = Cursors.Default` assignment from the editable ComboBox and verify the native I-beam cursor returns.
- [ ] If the hosted WinForms edit control does not inherit the expected cursor, set the editable area to `Cursors.IBeam` without changing the dropdown/button cursor.
- [ ] Check cursor visibility with the dropdown both open and closed and while suggestions are refreshed.
- [ ] Verify at normal and high DPI/scaling values and with both light and dark Windows cursor themes.
- [ ] Confirm that focus, mouse capture, and dropdown recreation do not leave the cursor hidden after moving away from the field.

## 5. Recalculate the selected item when rules change

- [ ] After `CategoryRuleSettingsChanged` saves the new in-memory rules, immediately re-evaluate the currently selected test item.
- [ ] Recalculate for individual checkboxes and once after a bulk header toggle completes.
- [ ] Do not run repeatedly for every child checkbox during a bulk toggle; use the existing `_loadingCategoryRules` guard and refresh once afterward.
- [ ] Recalculate when any relevant setting changes, including:
  - Rare Pickup or Store checkbox;
  - normal equipment Degree/type/gender checkbox;
  - supply Pickup or Store checkbox;
  - `Sell equipment not selected for storage`;
  - the general pickup options that participate in the displayed pickup decision.
- [ ] Only refresh when there is an unambiguous selected `ItemRuleTestOption`.
- [ ] If the user has only typed a partial or ambiguous display name, clear the old result or ask them to select an exact item instead of silently testing the first match.
- [ ] Keep the selected item object/codename stable while suggestions are rebuilt.

## 6. Verification matrix

### Rare versus normal storage

| Item | Rare Store | Normal D7 Store | Auto-sell unselected | Expected |
|---|---:|---:|---:|---|
| D7 Seal of Sun | Off | On | On | Sell |
| D7 Seal of Sun | Off | On | Off | Keep |
| D7 Seal of Sun | On | Off | On | Store |
| Normal D7 equipment | N/A | On | On | Store |
| Normal D7 equipment | N/A | Off | On | Sell |
| Normal D7 equipment | N/A | Off | Off | Keep |

- [ ] Repeat the rare cases for Seal of Star, Moon, Sun, Roc Set, Nova, Nova Set A, and Nova Set B.
- [ ] Test weapons, shields, accessories, and clothes.
- [ ] Verify that an explicit per-item Store filter still overrides category selections.
- [ ] Verify actual shopping behavior matches the tester result.
- [ ] Verify settings after save, application restart, reconnect, profile change, and character change.

### Tester behavior

- [ ] Select a D7 Seal of Sun item and leave it selected.
- [ ] Toggle its Rare Store checkbox and confirm the displayed result changes immediately between Store and Sell/Keep.
- [ ] Toggle normal D7 storage and confirm it does not change the selected rare item's result.
- [ ] Select a normal D7 item and confirm the same Degree toggle does update its result.
- [ ] Confirm the result never refers to a different item with the same display name; include the codename in diagnostics where useful.
- [ ] Confirm there is no stale result after changing any relevant checkbox.

## Relevant files

- `Library/RSBot.Core/Components/ItemCategoryRules.cs`
- `Library/RSBot.Core/Components/ShoppingManager.cs`
- `Library/RSBot.Core/Components/PickupManager.cs`
- `Plugins/RSBot.Items/Views/Main.CategoryRules.cs`
- `Plugins/RSBot.Items/Views/Main.cs`

## Completion criteria

- Rare items are controlled only by the Rare items tab.
- Normal equipment is controlled only by the Equipment tab.
- Tester output and real pickup/shopping behavior use the same decision logic.
- The suggestion dropdown remains visually stable while typing.
- The mouse cursor is visible and appropriate over the test field.
- The selected item's displayed decision updates immediately after every relevant rule change.

---

# Client launch, autologin, and log-view diagnostics TODO

## Review gate

This section is an implementation plan only. Do not change the logging or login code until the user has reviewed and approved the plan and the open decisions at the end of this document.

## Current findings

### File logging

- The file writer currently lives inside `Plugins/RSBot.Log/Views/Main.cs`, so file persistence depends on the Log UI plugin being constructed and active.
- File output currently happens only when `Kernel.Debug` is true. A normal Release build therefore does not create the detailed text log needed to diagnose failures on another machine.
- UI level filters are evaluated before file batching. A hidden log level is consequently also missing from the file.
- If the Log view's `Enabled` checkbox is off, pending entries are discarded instead of being persisted.
- The current file path changes from `Environment` to the character name after login, which splits a single launch/login attempt across multiple files.
- File-writer failures go only to `System.Diagnostics.Debug` and may be invisible to the user.

### Client launch

- `Start Client` calls regional authentication, `Game.Start()`, and then `ClientManager.Start()`.
- Client startup contains several sensitive stages: executable validation, suspended process creation, temporary loader configuration, optional XIGNCODE patching, DLL injection, thread resume, and the first client connection.
- These stages do not share a launch-attempt identifier, so messages from retries cannot be grouped reliably.
- Several native failures log only a generic message and omit the Win32 error code and message.
- `CreateProcess` does not currently expose `GetLastWin32Error`, and `STARTUPINFO.cb` is not visibly initialized before the native call. Both should be audited as part of implementation.
- Immediate process exit logs an exit code, but a later `Exited` event logs only `Client process exited!` without exit code, lifetime, launch phase, or whether RSBot intentionally killed it.
- Successful `LoadLibraryW` only proves that the DLL was loaded. The injected native library then performs configuration loading and hook installation without a persistent stage log.
- Native loader debug mode allocates a console but does not create a durable file trace.
- A text log can narrow down the failing phase, but it cannot always identify the exact native crash module or stack. Optional Windows crash-event/minidump collection may be needed if stage logging is insufficient.

### Autologin

- Autologin messages are spread across the UI, `AutoLogin`, gateway packet handlers, the pending-window queue, regional authentication services, and character selection.
- Several normal early returns are silent, including `Pending`, `_busy`, disabled autologin, missing secondary password, and some account/character-selection conditions.
- `AutoLogin.Handle()` is `async void`; an unexpected exception cannot be awaited by callers and can leave state difficult to diagnose.
- Some retries use `ContinueWith`, which makes cancellation, exception reporting, and attempt correlation harder.
- The current regional-auth debug logging can expose secrets such as access tokens, PIN/confirmation codes, HWID values, launcher IDs, or authentication response bodies. Diagnostic improvements must remove or redact these values rather than persist them more broadly.

### Log window jumping

- Logs are appended in 100 ms batches, which is useful and should be retained.
- Every visible append sets `SelectionStart` to the end and calls `ScrollToCaret()` unconditionally.
- This forces the view to the newest entry even when the user has scrolled upward, positioned the caret elsewhere, or selected text for copying.
- Removing old text at the 200,000-character limit can also disturb selection and scroll position.

## 7. Create an always-on core file-log sink

- [ ] Move file persistence out of the Log view into a core logging sink initialized early in application startup.
- [ ] Keep file logging active in both Debug and Release builds.
- [ ] Make UI visibility, UI severity filters, and the Log tab's `Enabled` checkbox affect display only, never file persistence.
- [ ] Preserve the non-blocking queue/batch writer pattern so gameplay and network threads never wait on disk I/O.
- [ ] Give every RSBot run a session ID and create one session log that covers startup, client launch, autologin, character entry, and later runtime events.
- [ ] Use a shareable path such as `Build/User/Logs/Sessions/<date>/<timestamp>_<RSBot-process-id>_<session-id>.log`.
- [ ] Include full date/time with milliseconds, severity, managed thread ID, subsystem, session ID, and optional operation ID on every line.
- [ ] Flush important Warning/Error/Fatal entries promptly while retaining batching for normal traffic.
- [ ] Flush and drain the queue during normal RSBot shutdown with a short bounded timeout.
- [ ] Implement size/age retention so logs cannot grow without limit; proposed default: 10 MB per file, rollover, and 30 days retention.
- [ ] If the primary path cannot be written, fall back to a safe temporary log path and show that exact path to the user.
- [ ] Never recursively log a file-writer failure through the same failing sink; use a guarded UI warning and `System.Diagnostics.Debug` fallback.
- [ ] Keep optional character-specific logs only as a secondary view if still useful; the session log must remain continuous.
- [ ] Add a visible `Open log folder` or `Copy current log path` action so the user can easily provide the correct file.

## 8. Add correlated client-launch diagnostics

- [ ] Generate a unique launch-attempt ID for every manual start and automatic restart, for example `[ClientLaunch:8F31C2]`.
- [ ] Carry the same ID through the General UI, `ClientManager`, process exit handling, proxy connection events, and native loader diagnostics.
- [ ] Log the reason for the launch: manual button, command-line launch, reconnect, or another explicit source.
- [ ] Record sanitized environment metadata once per attempt:
  - RSBot version/build and x86 architecture;
  - Windows version and process bitness;
  - selected `GameClientType`;
  - client executable path, file version, size, last-write timestamp, and preferably SHA-256;
  - `Client.Library.dll` version, size, timestamp, and SHA-256;
  - selected division/gateway indexes and loader-debug state;
  - never record passwords, tokens, full authentication command lines, or other credentials.
- [ ] Emit clear `BEGIN`, stage-success, stage-failure, and `END` messages for:
  1. button request and regional-auth completion;
  2. proxy/game preparation;
  3. path and dependency validation;
  4. mutex creation;
  5. suspended `CreateProcess`;
  6. temporary loader-config creation;
  7. optional XIGNCODE signature fetch and patch;
  8. remote memory allocation/write;
  9. remote `LoadLibraryW` thread creation and completion;
  10. primary thread resume;
  11. client process survival checkpoints;
  12. client-to-proxy connection and handshake.
- [ ] Set and validate `STARTUPINFO.cb` before `CreateProcess`.
- [ ] Ensure the `CreateProcess` P/Invoke has `SetLastError = true`; capture `Marshal.GetLastWin32Error()` immediately on failure and log both numeric and textual Win32 errors.
- [ ] Add equivalent native error detail to `WriteProcessMemory`, `CreateRemoteThread`, `ResumeThread`, `SuspendThread`, and cleanup operations where available.
- [ ] Do not treat `WaitForSingleObject`'s every nonzero result as the same failure; distinguish timeout, abandoned/failure, and log the native error where relevant.
- [ ] Record the created client PID and process start time as soon as they are available.
- [ ] Track the current launch phase so an unexpected exit says which phase was last completed.
- [ ] On process exit, safely capture:
  - exit code in hexadecimal and decimal;
  - runtime duration;
  - last completed launch phase;
  - whether the exit was user-requested/RSBot-requested or unexpected;
  - whether a proxy connection or game handshake had occurred.
- [ ] Add short survival checkpoints after resume (for example immediate, 500 ms, 2 s, and first proxy connection) without blocking the UI thread.
- [ ] Make cleanup idempotent and log whether RSBot terminated the process because setup failed.
- [ ] Include the launch-attempt ID in the user-facing failure message and point to the session log path.

## 9. Add native `Client.Library.dll` stage logging

- [ ] Pass the RSBot session ID, launch-attempt ID, and diagnostic log path through the temporary loader configuration.
- [ ] Start native logging in the worker thread used by `Initialize`; avoid heavy file I/O directly inside `DllMain`.
- [ ] Write and flush compact stage markers for:
  - DLL process attach and initialization thread start;
  - temporary config path discovery/open/read/delete;
  - parsed configuration validation;
  - Winsock initialization;
  - each detour/hook group installation;
  - detour transaction results;
  - initialization success or early return;
  - detach/uninstall when reachable.
- [ ] Log native error codes and failed Detours return values instead of assuming each installation succeeded.
- [ ] Ensure logging itself cannot crash or deadlock the game process; use guarded, append-only writes and avoid locks shared with hooks.
- [ ] Never include credentials, tokens, packet payload secrets, or the contents of authentication buffers.
- [ ] Correlate native lines with the same client PID and launch-attempt ID used by the managed log.

## 10. Optional Windows crash evidence

- [ ] After an unexpected early `sro_client.exe` exit, optionally query recent Windows Application Error / Windows Error Reporting events for that PID and time window.
- [ ] If available, append the faulting application, faulting module, exception code, and fault offset to the diagnostic log.
- [ ] Treat event-log access as best effort; log `unavailable` without failing client cleanup.
- [ ] If the text and Windows event logs are insufficient, offer an explicit opt-in diagnostic mode for a crash dump.
- [ ] Do not enable registry-based LocalDumps or create large dump files silently. Present the storage/privacy impact and require user approval first.
- [ ] Provide a simple note alongside any dump explaining that it may contain process memory and sensitive data.

## 11. Make autologin a clearly logged state machine

### Intermittent gateway-to-agent transition stall (observed 2026-09-11)

Observed behavior:

- The client starts and the automatic account-login request is sent normally.
- The gateway accepts the login and RSBot establishes the agentserver connection, but the flow can then stop before `Agent login response received` and before the character list arrives.
- In the captured failed attempt, the client exited after 37.7 seconds with exit code `0x0`; the automatic restart subsequently completed the same login flow successfully.
- The configured static captcha had length zero, but the server returned `Captcha entered successfully`, so the empty captcha was not the blocking step in this capture.
- `SocketException (995)` unobserved-task entries also appeared during the successful attempt. They are therefore most likely shutdown/cancellation noise from retired socket operations, not sufficient evidence of the login stall's root cause.

Planned fix:

- [ ] Start a correlated watchdog when the gateway login is accepted and/or the agentserver connection is established.
- [ ] Treat `Agent login response received`, character-list receipt, and successful character entry as distinct, timestamped progress checkpoints.
- [ ] If no agent-login response or character list arrives within a bounded interval (initial proposal: 10–15 seconds), log the exact stalled phase and elapsed time.
- [ ] Cancel the watchdog immediately when the expected checkpoint arrives, the user logs in manually, the connection closes, the client exits, or a newer login attempt supersedes it.
- [ ] Recover from a confirmed stall by closing the stale connection/client and scheduling one controlled automatic reconnect instead of waiting indefinitely.
- [ ] Use an attempt/generation identifier and a single-flight guard so a late packet from the old connection cannot cancel or advance the new attempt and multiple watchdogs cannot launch concurrent clients.
- [ ] Add a bounded retry count and backoff; after exhaustion, stop retrying and leave one actionable log entry rather than creating a restart loop.
- [ ] Record the outcome in one summary line: last completed phase, elapsed time, retry number, client PID/launch ID, and whether recovery was automatic.
- [ ] Separately suppress or downgrade expected socket-abort error `995` when it belongs to a deliberately retired/closed connection, while retaining unexpected socket failures as errors.
- [ ] Verify failed-first/successful-retry, normal first-attempt login, manual login, gateway queue, clientless login, deliberate client close, and late old-connection packet scenarios.

- [ ] Generate an autologin-attempt ID and link it to the triggering client-launch ID.
- [ ] Use a consistent prefix such as `[AutoLogin:<id>]` for every related message.
- [ ] Replace `async void AutoLogin.Handle()` with an awaitable `HandleAsync` flow, or an equivalent observed task with a top-level exception boundary.
- [ ] Reset `_busy`, cancellation resources, and pending state in `finally` blocks so exceptions cannot leave autologin stuck.
- [ ] Log state transitions rather than isolated messages:
  1. triggered and trigger reason;
  2. enabled/disabled check;
  3. saved-account lookup;
  4. server-list availability and selected server;
  5. server status/check/full handling;
  6. configured delay start, completion, or manual-login cancellation;
  7. sanitized login request sent;
  8. gateway response and mapped reason;
  9. queue entry/progress/exit;
  10. captcha challenge and response sent;
  11. secondary-password requirement and response sent;
  12. gateway/agent connection;
  13. character list received and selection reason;
  14. enter-game request and successful `OnEnterGame`;
  15. disconnect, retry schedule, cancellation, or final failure.
- [ ] Log silent early returns with an explicit reason at Debug or Notify level, without producing repeated spam.
- [ ] Mask account identity in logs, for example `ma***@domain` or a short stable hash; never write the full password.
- [ ] Never log static captcha text, secondary password, access/refresh tokens, PIN/confirmation codes, raw auth response bodies, MAC address, HWID, launcher ID, or regional-auth command-line credentials.
- [ ] Remove/redact existing sensitive messages in regional authentication services before enabling Release file logging.
- [ ] Log only safe metadata for auth calls: provider, step, HTTP status, duration, retryability, and sanitized error category.
- [ ] Replace unobserved `ContinueWith` retries with cancellable, awaited retry scheduling and log the retry number and delay.
- [ ] Add bounded retry counters/backoff where a packet response can repeatedly trigger another login attempt.
- [ ] Defensively handle a missing selected account/server/character and log a precise actionable reason instead of risking a null-reference failure.
- [ ] Distinguish manual login, automatic login, clientless login, and automatic reconnect in every attempt summary.
- [ ] End each attempt with one summary line containing outcome, duration, last state, retry count, and safe failure reason.

## 12. Stop the Log window from jumping during analysis

- [ ] Before appending a batch, determine whether the user is already following the end of the log.
- [ ] Auto-scroll only when the view was at or very near the bottom before the new batch arrived.
- [ ] If the user has scrolled upward, keep the same visible top line and do not move the caret or current text selection.
- [ ] Preserve an active text selection so the user can copy or inspect older entries while new logs arrive.
- [ ] When the user is not following the tail, show a lightweight `N new entries` indicator or `Resume live log` action.
- [ ] Clicking the indicator, pressing End, or manually scrolling back to the bottom should resume follow-tail mode.
- [ ] Preserve scroll position and selection when trimming old content at `MaxVisibleLogCharacters`; account for removed lines/characters.
- [ ] Continue batching UI updates instead of appending one line at a time.
- [ ] Avoid focus stealing, flicker, and unnecessary `SelectionStart` changes.
- [ ] Keep file logging completely independent of whether the user pauses the on-screen tail.
- [ ] If new UI controls require editing `Main.Designer.cs`, request explicit user confirmation first as required by the repository instructions; otherwise add them safely at runtime in `Main.cs`.

## 13. Logging verification plan

### Client launch

- [ ] Successful launch produces one correlated managed/native timeline from button click to proxy connection.
- [ ] Missing executable and missing `Client.Library.dll` produce actionable errors and a shareable file path.
- [ ] Forced `CreateProcess`, temp-config, injection, and resume failures identify the exact stage and native error.
- [ ] Immediate and delayed client crashes record PID, exit code, duration, last phase, and whether Windows supplied a faulting module/exception code.
- [ ] Manual `Kill Client` is labeled expected and is not reported as a crash.
- [ ] Automatic reconnect creates a new launch ID linked to the previous disconnect/autologin attempt.
- [ ] Release builds persist the same essential diagnostics as Debug builds.

### Autologin

- [ ] Verify disabled autologin, missing account, missing server, server check, full server, queue, wrong credentials, banned account, too many attempts, captcha, secondary password, character selection, and successful entry.
- [ ] Verify manual login cancels an outstanding delay cleanly and visibly.
- [ ] Verify reconnect and clientless paths use distinguishable attempt labels.
- [ ] Search generated logs for known test passwords, tokens, captcha values, PINs, account usernames, MAC/HWID values, and raw auth bodies; none may appear unredacted.
- [ ] Verify an unexpected exception produces one attempt failure summary and does not leave `_busy` or `Pending` stuck.

### Log UI and persistence

- [ ] Scroll several pages upward while logs arrive; the visible content must not move.
- [ ] Select and copy old text while logs arrive; selection must remain intact.
- [ ] Stay at the bottom; new messages should continue to follow automatically.
- [ ] Pause display or disable a UI severity filter; the complete file log must still contain the events.
- [ ] Generate enough data to trigger visible-text trimming and file rollover without a jump or data loss.
- [ ] Close RSBot with queued messages and verify the shutdown flush is bounded and complete.
- [ ] Simulate an unwritable primary log directory and verify the fallback path is clearly reported.

## Relevant logging and login files

- `Library/RSBot.Core/Log.cs`
- `Library/RSBot.Core/Components/ClientManager.cs`
- `Library/RSBot.Core/Extensions/NativeExtensions.cs`
- `Library/RSBot.Loader.Library/Library.cpp`
- `Plugins/RSBot.Log/Views/Main.cs`
- `Plugins/RSBot.General/Views/Main.cs`
- `Plugins/RSBot.General/Components/AutoLogin.cs`
- `Plugins/RSBot.General/Components/RuSroAuthService.cs`
- `Plugins/RSBot.General/PacketHandler/GatewayServerListResponse.cs`
- `Plugins/RSBot.General/PacketHandler/GatewayLoginResponse.cs`
- `Plugins/RSBot.General/PacketHandler/GatewayLoginRequestHook.cs`
- `Plugins/RSBot.General/PacketHandler/CaptchaDataResponse.cs`
- `Plugins/RSBot.General/PacketHandler/GlobalGatewayLoginAccepted.cs`
- `Plugins/RSBot.General/PacketHandler/CharacterListing.cs`

## Logging completion criteria

- A normal Release build always produces a shareable session log without depending on the Log tab.
- Every client launch/crash and autologin attempt has a unique, searchable correlation ID and a clear final outcome.
- Managed and native launch stages reveal the last successful step before an `sro_client.exe` failure.
- No passwords, tokens, captcha answers, secondary passwords, PINs, raw authentication bodies, or stable device identifiers are written to disk.
- The user can scroll, select, and copy historical logs without new entries moving the view.
- UI filtering or pausing never removes information from the persistent diagnostic log.

## Decisions to approve before implementation

- [ ] Confirm the proposed session-log location under `Build/User/Logs/Sessions`.
- [ ] Confirm the proposed default retention: 10 MB per file and 30 days.
- [ ] Decide whether the UI needs a `Resume live log / N new entries` control or whether silent scroll preservation is sufficient.
- [ ] Decide whether Windows Event Log lookup should be enabled automatically after unexpected early exits.
- [ ] Keep crash-dump collection disabled unless a later diagnostic run explicitly requires and authorizes it.

---

# Alchemy bot improvements TODO

## Review gate

This section records the currently observed enhancement bugs and a proposed configurable stone threshold. It is a plan only. Do not modify Alchemy code or generated Designer files until the user has reviewed and approved the behavior and layout below.

## Current enhancement behavior

- Lucky, Immortal, Astral, and Steady stones are currently considered only when the item's current `OptLevel` is at least 5.
- Consequently, the first protected enhancement attempt is +6.
- The hard-coded condition `item.OptLevel >= 5` appears separately for every stone in `EnhanceBundle.Run`.
- Before an elixir attempt, missing enabled stone options are fused in this fixed order:
  1. Steady;
  2. Lucky;
  3. Immortal;
  4. Astral;
  5. elixir plus lucky powder.
- Astral additionally requires the item to have the matching Immortal magic option.
- Only a stone matching the target item's degree is selected.

## 14. Fix known Alchemy enhancement bugs

### Wrong checkbox cleared when Immortal stone is unavailable

Current issue in `EnhanceSettingsView.PopulateView`:

- when no matching Immortal stone exists, the code clears `checkUseAstralStones`;
- it should clear or otherwise update `checkUseImmortalStones` instead;
- this can silently change the Astral setting and leave the Immortal checkbox state misleading.

Tasks:

- [ ] Replace the incorrect Astral-checkbox assignment with the correct Immortal-checkbox handling.
- [ ] Make availability handling consistent for all four stones.
- [ ] Decide whether temporary inventory absence should uncheck the user's preference or merely disable the control. Preferred behavior: preserve the preference and disable the row while unavailable, so obtaining another stone restores the configured behavior.
- [ ] Ensure programmatic checkbox/availability refresh does not repeatedly rebuild a partially inconsistent `EnhanceBundleConfig` through `CheckedChanged` events.
- [ ] Refresh the count, enabled state, and configured state atomically when the selected item or inventory changes.

### Null lucky-powder handling

Current issue in `AlchemyManager.TryFuseElixir`:

- `powder` is declared nullable;
- `SendFusePacket` can pass `null` when no lucky powder exists and `Stop if 0 lucky powder` is disabled;
- `TryFuseElixir` accesses `powder.Slot` before checking for null, which can throw instead of attempting enhancement without powder.

Tasks:

- [ ] Never dereference `powder` before a null check.
- [ ] Resolve `powderInInventory` only when a powder was supplied.
- [ ] Keep the intended two-slot packet for item + elixir when powder is null and the three-slot packet when powder exists.
- [ ] Verify that enhancer/proof-item detection is performed only when a third ingredient exists.
- [ ] Add a clear log entry stating whether the attempt uses lucky powder, a degree-12+ proof/enhancer, or no powder.
- [ ] Verify `Stop if 0 lucky powder = On` stops before sending any fusion packet.
- [ ] Verify `Stop if 0 lucky powder = Off` safely continues without powder.

### Related Astral validation

- [ ] Verify whether Astral requires only the presence of the degree-matching Immortal option or a particular Immortal value/count.
- [ ] Make the implementation and log message agree; the current message says the immortality option is "not high enough", while the code only checks whether one expected option ID exists.
- [ ] Do not silently mutate `_config.UseAstralStones` when the dependency is missing. Keep the user's preference, skip the current Astral fusion, and report an actionable reason without log spam.

## 15. Required enhancement protection rules

These rules replace the earlier proposal that every stone have an independently configurable threshold.

### Before the +5 attempt

- [ ] Once the item has reached **+4**, the bot must prepare Immortal before attempting +5.
- [ ] After Immortal is present, the bot must prepare Astral before attempting +5.
- [ ] The order is mandatory: **Immortal first, then Astral**.
- [ ] If the item is missing either option but the corresponding stone exists in the inventory, fuse the missing stone and wait for confirmation before continuing.
- [ ] If the item is missing either option and the corresponding stone is not available in the inventory, do not send an enhancement/elixir packet. Stop or remain blocked with an actionable message.
- [ ] If the item already has the required option, do not consume another stone.
- [ ] A checkbox must not disable these safety requirements; the required protection rule is fixed and always active for the relevant plus range.

### From +5 onward

- [ ] Before every attempt above +5 (starting with the +6 attempt), the item must have the Steady option.
- [ ] If Steady is missing but a matching Steady stone exists, fuse it and wait for confirmation.
- [ ] If Steady is missing and no matching Steady stone exists, do not attempt the enhancement.
- [ ] If an enhancement failure permanently reduces durability, keep the item and its new durability state; do not treat durability loss as a reason to bypass the Steady requirement.
- [ ] Verify the exact server behavior after a failed +5-or-higher attempt and ensure the bot does not send a follow-up attempt before the item state is refreshed.

### Lucky stone: the only configurable threshold

- [ ] Add only one numeric setting, `LuckyUseFromPlus`, to `EnhanceBundleConfig`.
- [ ] Interpret the value as the first target enhancement attempt that uses Lucky: value `6` means before +6.
- [ ] Keep the default at **+6** to preserve the current behavior unless the user chooses another value.
- [ ] Lucky remains optional: its checkbox controls whether it may be used, and a missing Lucky stone does not block enhancement unless a separate future safety option is explicitly added.
- [ ] Do not add configurable thresholds for Immortal, Astral, or Steady; their requirements are fixed by the rules above.

### Fixed order and state machine

- [ ] Calculate `nextPlusValue = item.OptLevel + 1` before deciding which protection is required.
- [ ] For `nextPlusValue == 5`, require Immortal, then Astral.
- [ ] For `nextPlusValue >= 6`, require Steady, while retaining the Immortal and Astral options from the +5 preparation.
- [ ] Apply Lucky independently when `nextPlusValue >= LuckyUseFromPlus` and the Lucky option is enabled.
- [ ] After any stone fusion, wait for the inventory/item update and reevaluate requirements from the refreshed item. Never send the enhancement packet from the stale item object.
- [ ] If a required fusion cannot be completed, remain blocked/stopped; do not fall through to `SendFusePacket()`.
- [ ] Log one clear reason whenever enhancement is blocked, including item plus, required option, stone availability, and target attempt.

## 16. Investigate the configured +5 limit being ignored

The user observed that `Max enhancement = +5` still allowed another enhancement after the item reached +5. The current code checks `config.Item.OptLevel >= config.MaxOptLevel`, so this needs a state-refresh/configuration investigation before implementation.

- [ ] Log at every enhancement tick: item instance/slot, current `OptLevel`, `MaxOptLevel`, target `nextPlusValue`, and the active configuration identity.
- [ ] Log the item-update event after a successful +5 result and verify that `EnhanceBundleConfig.Item` is replaced with the refreshed item before `_shouldRun` is set true.
- [ ] Verify that UI `config_CheckedChange` events do not rebuild the configuration with a stale item or an unintended `MaxOptLevel` after the enhancement response.
- [ ] Ensure the max-level guard runs again after every stone fusion and every elixir result, before any packet is sent.
- [ ] Add a final pre-send guard immediately before `TryFuseElixir`: if the refreshed item is already at or above Max, stop and send no packet.
- [ ] Make the stop reason explicit in the log: `Reached max enhancement +5; no +6 request sent.`
- [ ] Test max values +1, +4, +5, and +8, including a successful transition exactly onto the configured maximum.
- [ ] Test a failure that lowers OptLevel and a failure that changes durability, ensuring the bot reevaluates the refreshed state before deciding whether another attempt is allowed.

## 17. Proposed enhancement-settings layout

The supplied screenshot confirms usable horizontal space to the right of the current stone counts. Keep the four checkbox rows and counts, but add only one editable threshold column for Lucky.

Proposed layout:

| Use | Available | Use Lucky from + |
|---|---:|---:|
| ☑ Lucky stones | x100 | `[ 6 ]` |
| ☑ Immortal stones | x100 | fixed: +5 required |
| ☑ Astral stones | x100 | fixed: +5 required, after Immortal |
| ☑ Steady stones | x100 | fixed: +6 and above required |

Layout tasks:

- [ ] Add a compact header above the right column: **`Use Lucky from +`**.
- [ ] Add one small numeric input aligned only with the Lucky row.
- [ ] Show concise fixed-rule hints for the other rows, either as read-only text or tooltips, not editable numeric fields.
- [ ] Increase the horizontal gap between the stone labels and count labels so long translated captions do not collide.
- [ ] Keep all four count labels vertically and horizontally aligned.
- [ ] Keep the Lucky input aligned with the count column and disable it when Lucky is unchecked.
- [ ] When a required stone is unavailable, visually mark the row as blocked rather than silently changing the fixed rule.
- [ ] Use a `TableLayoutPanel` or equivalent layout container if practical so localization, DPI scaling, and resized plugin views remain aligned.
- [ ] Add a tooltip: `6 = apply Lucky before attempting +6. Immortal/Astral are required before +5; Steady is required from +6.`
- [ ] Preserve a clean tab order: checkbox, Lucky threshold (Lucky row only), then the next row.
- [ ] Because `EnhanceSettingsView.Designer.cs` is generated and repository instructions forbid editing Designer files without explicit confirmation, obtain that confirmation before implementing this layout. Prefer using the WinForms Designer rather than hand-editing generated code.

## 18. Alchemy verification matrix

### Required protection behavior

| Current item | Target attempt | Required state | Missing required stone | Expected |
|---:|---:|---|---|---|
| +4 | +5 | Immortal, then Astral | No | Fuse Immortal, then Astral, then attempt +5 |
| +4 | +5 | Immortal or Astral unavailable | Yes | Block; send no enhancement packet |
| +5 | +6 | Steady | No | Fuse Steady, then attempt +6 |
| +5 | +6 | Steady unavailable | Yes | Block; send no enhancement packet |
| +6+ | next attempt | Steady present | N/A | Enhancement may proceed if Max has not been reached |

### Lucky behavior

| Target attempt | Lucky threshold | Lucky enabled | Expected |
|---:|---:|---:|---|
| +5 | 6 | Yes | No Lucky requirement yet |
| +6 | 6 | Yes | Apply missing Lucky before enhancement |
| +7 | 8 | Yes | No Lucky requirement yet |
| +8 | 8 | Yes | Apply missing Lucky before enhancement |
| any | any | No | Do not use Lucky; fixed safety rules still apply |

- [ ] Verify +4 → +5 always prepares Immortal before Astral.
- [ ] Verify missing Immortal or Astral blocks the enhancement packet.
- [ ] Verify +5 → +6 always requires Steady and blocks when Steady is unavailable.
- [ ] Verify the required rules remain active regardless of checkbox state.
- [ ] Verify Lucky follows only its configured threshold and checkbox.
- [ ] Verify a failed enhancement and permanent durability decrease cause a fresh item-state reevaluation before any next attempt.
- [ ] Verify no stone is reapplied when its option is already present.
- [ ] Verify wrong-degree stones and depleted stacks are treated as unavailable.
- [ ] Verify default Lucky threshold +6 preserves the current Lucky timing.

### Bug regressions

- [ ] Removing all Immortal stones must never clear the Astral checkbox.
- [ ] Removing one stone type must not change another stone type's preference.
- [ ] Enhancing without lucky powder must not throw when stopping on zero powder is disabled.
- [ ] No fusion packet may be sent after the bot has stopped due to zero lucky powder.
- [ ] A configured Max enhancement of +5 must send no +6 request after a successful +5 result.
- [ ] UI counts, enabled states, checkboxes, Lucky threshold, and the active `EnhanceBundleConfig` must agree after every refresh.

## Relevant Alchemy files

- `Botbases/RSBot.Alchemy/Bundle/Enhance/EnhanceBundleConfig.cs`
- `Botbases/RSBot.Alchemy/Bundle/Enhance/EnhanceBundle.cs`
- `Botbases/RSBot.Alchemy/Views/Settings/EnhanceSettingsView.cs`
- `Botbases/RSBot.Alchemy/Views/Settings/EnhanceSettingsView.Designer.cs`
- `Botbases/RSBot.Alchemy/Helper/AlchemyItemHelper.cs`
- `Library/RSBot.Core/Components/AlchemyManager.cs`

## Alchemy completion criteria

- Absence of an Immortal stone never changes the Astral setting.
- Elixir enhancement safely supports an intentionally missing lucky powder.
- Immortal and Astral are mandatory before the +5 attempt, in that order.
- Steady is mandatory for every attempt above +5.
- Only Lucky has an editable first-use plus level; the default is +6.
- Missing required stones block enhancement instead of allowing an unsafe packet.
- A configured Max enhancement of +5 reliably stops at +5.
- UI preferences and the Lucky threshold survive availability refreshes without silently changing.
- Astral dependency handling is explicit, stable, and does not mutate user configuration.
- The four stone rows remain readable and aligned at supported DPI and localization settings.

---

# Ability-pet pickup TODO

## Reported symptom

When `Items → Pickup settings → Use ability pet to pickup items` is enabled and a GrabPet is summoned, some items are still picked up by the character instead of the pet.

## Current code finding

There is a likely actor-arbitration race in the current pickup flow:

- `LootBundle.Invoke()` starts `RunAbilityPet(...)` and returns only when it starts a new pet pickup run.
- While `RunningAbilityPetPickup` is already true, that condition becomes false and `LootBundle.Invoke()` can fall through to `RunPlayer(...)` in the same loop.
- `RunAbilityPet` is `async void`, so it returns to the caller immediately while the pet pickup work continues.
- `RunPlayer` itself does not reject a player pickup when an active ability pet is configured and available; it only passes a filtering flag to `Condition`.
- The 200 ms UI timer also starts `RunAbilityPet` independently when the bot is not running, so two scheduling paths can request pet pickup.
- The Loot bundle caches `UseAbilityPet` in `LootConfig`; changing the Items checkbox while the bot is already running may not refresh that cached value until the next bundle refresh.

## 19. Make the pickup actor exclusive

- [ ] Define one central actor-selection rule: when `UseAbilityPet` is enabled and an active ability pet exists, **all eligible ground-item pickup must be pet-only**.
- [ ] In `LootBundle.Invoke()`, return immediately whenever the ability-pet mode is enabled and an active pet exists, regardless of whether `RunningAbilityPetPickup` is currently true.
- [ ] If a pet pickup run is already active, simply skip this tick; never fall through to `RunPlayer`.
- [ ] Add a defensive guard inside `PickupManager.RunPlayer` as well, so another caller cannot make the character pick up while pet-only mode is active.
- [ ] Keep character pickup available only when the setting is disabled or no active ability pet exists, unless the user explicitly approves a fallback mode.
- [ ] Use one scheduling path for pet pickup, or coordinate the Loot bundle and the 200 ms timer through a single scheduler. Avoid two independent `RunAbilityPet` entry points.
- [ ] Replace `async void RunAbilityPet` with an awaitable/observable operation where practical, or at minimum make the running state and exceptions externally observable.
- [ ] Capture the active pet reference/UniqueId at the start of a run and verify it remains active before each pickup request.
- [ ] If the pet is unsummoned, replaced, disconnected, or its inventory becomes unavailable during a run, abort the pet run and do not fall back to the character in the same tick.
- [ ] Handle pet inventory-full responses explicitly and log the actor decision; do not silently switch to character pickup while pet-only mode is enabled.
- [ ] Refresh the effective `UseAbilityPet` setting immediately when the Items checkbox changes, or make the Loot bundle read the current setting instead of a stale cached value.
- [ ] Preserve ownership, pickup filters, training-area radius, obstacle, party-share, and `PickOnlyChar` semantics while changing only the pickup actor arbitration.

## 20. Ability-pet diagnostics

- [ ] Add debug logs for each pickup decision with actor (`AbilityPet` or `Player`), setting state, active pet UniqueId, running flags, and item UniqueId/codename.
- [ ] Log when `RunAbilityPet` starts, skips because another run is active, aborts because the pet disappeared, completes, or fails.
- [ ] Log whenever `RunPlayer` is blocked because pet-only mode is active.
- [ ] Include the reason when no active ability pet is detected even though the setting is enabled.
- [ ] Throttle repeated per-tick messages so normal farming does not flood the log.

## 21. Ability-pet verification matrix

- [ ] Pet enabled, GrabPet summoned, bot running: every eligible item is requested by the pet; the character sends no pickup request.
- [ ] Pet pickup already running: later Loot ticks do not start character pickup.
- [ ] Pet enabled, pet unsummoned: behavior is explicit and logged; choose whether to wait/block or allow character fallback only by an approved setting.
- [ ] Pet setting disabled, pet summoned: character pickup continues normally.
- [ ] Checkbox changed while bot is running: the next pickup decision uses the new value immediately.
- [ ] Pet disappears halfway through a run: no stale pet reference is used and no same-tick player fallback occurs.
- [ ] Pet inventory full: no character pickup occurs in pet-only mode; the user receives a clear diagnostic.
- [ ] Verify normal pickup filters, ownership rules, obstacle handling, party item sharing, and training radius in both actor modes.
- [ ] Verify the behavior through both the Training Loot bundle and the non-bot 200 ms timer path.

## Relevant pickup files

- `Library/RSBot.Core/Components/PickupManager.cs`
- `Botbases/RSBot.Training/Bundle/Loot/Lootbundle.cs`
- `Botbases/RSBot.Training/Views/Main.cs`
- `Plugins/RSBot.Items/Views/Main.cs`
- `Library/RSBot.Core/Objects/Cos/Ability.cs`
- `Library/RSBot.Core/Network/Handler/Agent/Cos/CosUpdateResponse.cs`

## Ability-pet completion criteria

- With the setting enabled and a GrabPet active, the character never picks up an eligible item as a side effect of a concurrent or already-running pet pickup operation.
- The actor decision is consistent regardless of which scheduler initiated the pickup.
- Pet disappearance, pet inventory-full, and stale configuration states are explicit and diagnosable.
- Existing item-selection and ownership rules remain unchanged.
