
---

## 1. Match Sales Name in UI Against Bloomberg Name from Clipboard ✅

**Goal:** When a Bloomberg chat message is parsed, automatically resolve the
counterpart/sales person name from the raw Bloomberg chat handle or display name
to the corresponding internal Sales name used in the UI dropdowns.

**Tasks:**
- [x] Build or extend a mapping table (`BloombergNameToSalesFullName`) that maps
      Bloomberg display names to `ReferenceData` Sales full names.
      → Added `IReadOnlyDictionary<string, string> BloombergNameToSalesFullName`
        to `ReferenceData`. Populated in `DatabaseService.LoadReferenceDataAsync()`
        by extending the `userprofile` SELECT to include `BloombergName` and
        mapping `BloombergName → FullName` (case-insensitive).
- [x] In `OvmlBuilderAP3` / `OvmlBuilder`, extract the sender name from the
      clipboard text header and expose it on `OvmlLeg.SenderName`.
      → Added `SenderName = ""` optional parameter to the `OvmlLeg` record.
        Both parsers share a `RxSenderName` regex that matches an ALL-CAPS
        full name on its own line at the top of the clipboard text
        (e.g. `"MATZ ERIKSSON"`). `OvmlBuilderAP3.TryParse` stamps the name on
        all legs synchronously; `OvmlBuilder.TryParseAsync` stamps it after the
        AI parse succeeds.
- [x] In `MainViewModel.PopulateLegsFromParsed()`, resolve `OvmlLeg.SenderName`
      through `BloombergNameToSalesFullName` and pre-fill `TradeLegViewModel.Sales`
      and `TradeLegViewModel.ReportingEntity` when a match is found.
      → `resolvedSales` and `resolvedReporting` are derived from the mapped
        Sales user's profile (`FullNameToUserId` → `UserIdToReportingEntity`).
        Both are applied **after** `vm.InvestmentDecisionID` and
        `vm.ApplyFromOvmlLeg()` so they win over any value written by
        `OnInvestmentDecisionIDChanged`.
- [x] If no match is found, leave `Sales` at its current default and surface a
      yellow warning in the status bar.
      → When `SenderName` is non-empty but not found in the map,
        `SetStatusAsync` emits `"⚠ Sales name '…' not found in mapping table
        — please set Sales manually."` and the existing Windows-user default
        is preserved unchanged.
- [x] Fallback: when no sender name is present in the clipboard text, fall back
      to existing `Environment.UserName`-based logic unchanged.

**Bugs fixed during implementation:**
- `OnInvestmentDecisionIDChanged` was overwriting `Sales` after it was set →
  fixed by applying `Sales` and `ReportingEntity` last in `PopulateLegsFromParsed`.
- `PortfolioMX3` was cleared when the parsed pair equalled the field default
  (`"EURSEK"`) because CommunityToolkit's `SetProperty` skips unchanged values,
  so `OnCurrencyPairChanged` never fired → fixed by calling `LoadPortfolioAsync`
  unconditionally in `ApplyFromOvmlLeg` after assigning the pair.
- `LoadPortfolioAsync` always did a live DB round-trip; replaced with a fast
  synchronous lookup against `ReferenceData.CurrencyToPortfolio` with a DB
  fallback for pairs added after startup.

---

## 2. DateTimePicker for Expiry Date in ClipboardCaptureDialog

**Goal:** Replace the plain read-only expiry text label in `ClipboardCaptureDialog`
with an interactive `DatePicker` (or a third-party `DateTimePicker`) so the user
can correct a parsed expiry date before clicking "Fill Form".

**Tasks:**
- [ ] Add a `DatePicker` (WPF built-in) or a suitable third-party control
      (e.g. `Xceed.Wpf.Toolkit.DateTimePicker`) to the dialog XAML in place of
      `ExpiryLabel`.
- [ ] Bind the picker to a `SelectedDate` property on the dialog or on each
      `LegRow`, defaulting to the parsed expiry date.
- [ ] When the dialog is multi-leg and expiry dates differ across legs, show a
      per-leg date picker in the legs list instead of a single header picker.
- [ ] Propagate the (potentially user-edited) expiry date through `LegRow` →
      `ToOvmlLeg()` so that `RebuildOvmlFromCurrentLegs()` uses the corrected date.
- [ ] Ensure the corrected date is also forwarded to `TradeLegViewModel.ExpiryDate`
      when the user confirms with "Fill Form" or "Both".

---

## 3. Weekend & Holiday Warning on Expiry Date

**Goal:** After the user clicks "Fill Form" (or the equivalent confirm action),
detect if the resolved expiry date falls on a weekend or a local market holiday
and ask whether to roll the date backward, forward, or keep it (non-bookable).

**Tasks:**
- [ ] **Weekend check:** After the user confirms in `ClipboardCaptureDialog`,
      inspect each leg's expiry date. If it lands on Saturday or Sunday, show a
      `MessageBox` / modal dialog:
      - "Expiry {date} falls on a weekend. Roll to previous business day,
        next business day, or keep (not bookable)?"
      - Apply the chosen roll direction to `TradeLegViewModel.ExpiryDate`.
- [ ] **Holiday check:** Leverage the existing `Holidays` `DataTable` already
      loaded in `MainViewModel` (columns `Market` / `HolidayDate`) to determine
      whether the expiry date is a holiday for the relevant currency-pair markets.
- [ ] Apply holiday warnings inside `TradeLegViewModel` (e.g. in
      `ApplyExpiryInput` or a new `ValidateExpiryDate()` method):
      - Set a new `[ObservableProperty] bool ExpiryIsHoliday` flag.
      - Apply a red border to the expiry cell via a `DataTrigger` bound to the
        flag (in `TradeLegControl.xaml` or `TradeGridControl.xaml`).
      - Attach a tooltip with the holiday description (market name + holiday name
        if available).
- [ ] Expose holiday name/description in the `DataTable` if not already present
      (extend the DB query in `DatabaseService.LoadHolidaysAsync()`).
- [ ] Add a helper method `Calendar.IsHoliday(DateTime date, DataTable holidays,
      IEnumerable<string> markets)` (or extend the existing `Calendar.cs`) that
      returns `(bool isHoliday, string description)`.

---

## 4. Restore Original Bloomberg Clipboard Content After OVML Parse ✅


**Goal:** After the OVML string has been parsed (or when the user closes
`ClipboardCaptureDialog` without acting), restore the original Bloomberg
clipboard text that was overwritten during parsing/pasting, so the user's
Bloomberg workflow is not interrupted.

**Tasks:**
- [x] In `ClipboardWatcher` / `MainViewModel.RunClipboardFlowAsync()`, snapshot
      the raw clipboard content (text) immediately before any parse or paste
      operation begins.
      → `_clipboardSnapshot` captured on UI thread at the start of
        `RunClipboardFlowAsync`. If snapshot equals `e.Text` (the Bloomberg chat
        that just triggered the flow) it is set to `null` — there is no earlier
        content to restore, and restoring the chat text would cause Bloomberg
        Terminal to receive the wrong clipboard content on `Ctrl+V`.
- [x] Pass the snapshot into `ClipboardCaptureDialog` (or keep it in the ViewModel)
      so it can be restored on dialog close.
      → Kept in `MainViewModel._clipboardSnapshot`; no dialog change needed.
- [x] On `ClipboardCaptureDialog.OnClosed` (for every result — Reject, PopulateUi,
      or Both):
      - Set `_suppressClipboardEvents = true` before restoring.
      - Restore the clipboard via `Clipboard.SetText(originalText)`.
      - Schedule clearing the suppress flag after the restore (same pattern as
        `SendToBloombergAsync`).
      → `MainWindow.OnClipboardCaptureDialogRequested` calls
        `vm.RestoreClipboardAsync()` for `Reject` and `PopulateUi`.
        `Both` / `OpenInBloomberg` delegate to `SendToBloombergAsync` which
        calls `RestoreClipboardAsync()` in its `finally` block.
        `RestoreClipboardAsync` also calls `ClipboardWatcher.ResetLastSignature()`
        after the write settles, so the same text can be re-copied immediately.
- [x] Ensure this also runs when `SendToBloombergAsync` completes (after the OVML
      is pasted, restore the original text).
      → `SendToBloombergAsync` calls `RestoreClipboardAsync(delayMs: 1000)` in
        its `finally` block. The 1000 ms delay ensures Bloomberg has consumed
        the OVML from the clipboard via `Ctrl+V` before it is overwritten.
- [x] Handle edge cases: clipboard content may have changed to something else
      while the dialog was open — decide policy (restore anyway vs. skip).
      → **Policy: restore anyway** for `Reject` / `PopulateUi` (no Bloomberg
        paste involved). For `OpenInBloomberg` / `Both`, snapshot is `null`
        when the chat text was the most recent clipboard content, so
        `RestoreClipboardAsync` is a no-op — Bloomberg Terminal always gets
        the OVML string uninterrupted.

---

## 5. SaveResultDialog — Show Bank's Perspective, Not the Client's ✅

**Goal:** In `SaveResultDialog`, all trade direction labels should be
expressed from the bank's (our) perspective, not the client's.

**Tasks:**
- [x] Audit the `SaveResultItem` construction in `MainViewModel.SaveAsync()` and
      `TradeSubmitResult` to identify where "Client pays / receives" language
      originates.
      → Both `BuySell` and `HedgeBuySell` on `TradeLeg` are confirmed client-side
        (same inversion already applied in `TradeIngestService` before STP submission).
- [x] Invert `BuySell` and `HedgeBuySell` in `OnSaveResultDialogRequested`
      (`TradeGridControl.xaml.cs`) when building `optionLabel` and `hedgeLabel`,
      so the dialog reflects the bank's direction for both options and hedges.
      → `BankPerspectiveLabel` approach was dropped in favour of a direct inline
        inversion — simpler, no new helper needed.
- [x] Update `SaveResultDialog.xaml` to clearly indicate the perspective.
      → Added a discrete blue badge — `"🏦 Directions shown from Bank's perspective"`
        — above the results list using `AccentBlueMutedBrush` / `AccentBlueBrush`
        from the existing theme. Badge is separate from the leg rows.
- [ ] Also update `TotalPremiumDisplay` in `MainViewModel.UpdateTotalPremium()`
      if it is displayed anywhere in the results dialog — change "Client receives"
      / "Client pays" to "Bank pays" / "Bank receives".
      → Deferred: `TotalPremiumDisplay` is shown in the main grid distributor
        column, not in `SaveResultDialog`. Revisit if it is ever surfaced there.

---

## 6. Assume Next-Year Expiry When Parsed Date Has Already Passed ✅

**Goal:** If the OVML parser resolves an expiry date that is in the past
(relative to today), automatically roll it forward by one year, since Bloomberg
chat messages often omit the year.

**Tasks:**
- [x] In `OvmlBuilderAP3.ExtractExpiryOrTenor()`: when no year is present in the
      matched expiry pattern (`"expiry 15 mar"`), check if the resolved date is
      in the past and roll forward one year via `dt.AddYears(1)`.
      → `yearExplicit` flag guards the roll — explicit years (e.g. `"expiry 15 mar 2026"`)
        are never touched.
- [x] In `OvmlBuilder.GenerateAsync()` (AI parser): added `RollExpiryIfPast()`
      called after `NormalizePair` on both pass 1 and pass 2.
      → Handles both `po.Expiry` (single shared date) and `po.Expiries` (per-leg
        dates). Only acts on `yyyy-MM-dd` strings — tenors (`"3M"`, `"ON"`) are
        left untouched.
- [x] `ExpiryDateParser.TryParseDate()` already rolled `dd/MM` and `dd-MMM`
      formats (both assume current year when no year is given). No change needed
      there.
- [x] `TradeLegViewModel.ApplyExpiryInput()`: central fallback guard added — if
      a fully-resolved `Convention.ExpiryDate` is still in the past after parsing,
      re-parse with `ExpiryDate.AddYears(1)` as a `yyyy-MM-dd` string, and set
      `_parent.StatusMessage` to `"ℹ Expiry rolled to next year (yyyy-MM-dd)"`.
      → Catches any remaining edge cases regardless of which parser produced the
        date, with a visible status-bar hint for the trader.
- [ ] ~~Add unit tests covering: past date → next year, today → unchanged,
      future date → unchanged, leap-year edge case (Feb 29).~~


---

## 7. Add Menu Bar with "File" and "Options" Items

**Goal:** Add a standard WPF `Menu` bar to `MainWindow.xaml` with:
- **File** — existing top-level actions (Open Recent, Clear All, Save, Send Mail).
- **Options > Send to OVML** — generates OVML syntax from all current trades in
  the UI and pastes it directly to the Bloomberg Terminal window.

**Tasks:**

### 7a. Menu bar scaffold
- [ ] Add a `<Menu>` element to `MainWindow.xaml` above (or integrated into) the
      existing toolbar/header area, using the dark-blue theme brush resources for
      consistent styling.
- [ ] Add **File** menu with items:
      - Open Recent (`OpenRecentCommand`)
      - Separator
      - Clear All (`ClearAllCommand`)
      - Save (`SaveCommand`)
      - Send Mail (`SendMailCommand`)
- [ ] Add **Options** menu initially with only the "Send to OVML" item.

### 7b. "Send to OVML" command
- [ ] Add `[RelayCommand] private async Task SendAllToBloombergAsync()` in
      `MainViewModel`.
- [ ] Collect all current `TradeLegViewModel` instances, call `leg.ToModel()`,
      and feed the models to `OvmlBuilderAP3` (or the active parser) to generate
      a combined OVML string for the full trade (multi-leg aware).
- [ ] Call `SendToBloombergAsync(combinedOvml)` to paste the generated string to
      the Bloomberg Terminal (reusing the existing `_bloombergPaster` flow).
- [ ] Validate that at least one leg exists and all required fields are filled
      before generating; otherwise show a tooltip/status warning.
- [ ] Disable the menu item when `_bloombergPaster` is `null` (not configured).

---

## General / Cross-Cutting

- [ ] Review all new user-facing strings for consistent English phrasing and
      alignment with existing status message style (✓ / ⚠ / ⏳ prefix convention).
- [ ] Ensure all new `async` commands use `.FireAndForget(onError: ...)` or
      proper `try/catch` so exceptions do not silently escape.
- [ ] Add XML doc comments to all new public methods and properties.