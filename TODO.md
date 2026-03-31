# TODO — FxTradeConfirmation Development Backlog

---

## 1. Match Sales Name in UI Against Bloomberg Name from Clipboard

**Goal:** When a Bloomberg chat message is parsed, automatically resolve the
counterpart/sales person name from the raw Bloomberg chat handle or display name
to the corresponding internal Sales name used in the UI dropdowns.

**Tasks:**
- [ ] Build or extend a mapping table (e.g. `BloombergNameToSalesMap`) that maps
      Bloomberg login names / display names to `ReferenceData` Sales IDs.
      Consider loading this from the database alongside other reference data in
      `DatabaseService.LoadReferenceDataAsync()`.
- [ ] In `OvmlBuilderAP3` / `OvmlBuilder`, extract the sender name from the
      clipboard text header (e.g. `"From: John Doe (JDOE@Bloomberg)"`) and
      expose it on `OvmlLeg` or as a top-level parse result.
- [ ] In `MainViewModel.PopulateLegsFromParsed()`, after parsing, attempt to
      resolve `OvmlLeg.SenderName` through the mapping table and pre-fill
      `TradeLegViewModel.Sales` when a match is found.
- [ ] If no match is found, leave `Sales` at its current default and optionally
      surface a yellow warning in the status bar.
- [ ] Write unit tests for the name-resolution logic with known Bloomberg handles.

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

## 5. SaveResultDialog — Show Bank's Perspective, Not the Client's

**Goal:** In `SaveResultDialog`, all premium / trade direction labels should be
expressed from the bank's (our) perspective, not the client's.

**Tasks:**
- [ ] Audit the `SaveResultItem` construction in `MainViewModel.SaveAsync()` and
      `TradeSubmitResult` to identify where "Client pays / receives" language
      originates.
- [ ] Introduce a helper or convention, e.g. `BankPerspectiveLabel(BuySell buySell,
      decimal premiumAmount)`, that maps:
      - Client BUY → Bank SELL → "We sell {CallPut} — We receive {premium}"
      - Client SELL → Bank BUY → "We buy {CallPut} — We pay {premium}"
- [ ] Update `SaveResultDialog.xaml` detail rows to use the new bank-perspective
      labels.
- [ ] Also update `TotalPremiumDisplay` in `MainViewModel.UpdateTotalPremium()`
      if it is displayed anywhere in the results dialog — change "Client receives"
      / "Client pays" to "Bank pays" / "Bank receives".

---

## 6. Assume Next-Year Expiry When Parsed Date Has Already Passed

**Goal:** If the OVML parser resolves an expiry date that is in the past
(relative to today), automatically roll it forward by one year, since Bloomberg
chat messages often omit the year.

**Tasks:**
- [ ] In `OvmlBuilderAP3` (and `OvmlBuilder`) after resolving `ExpiryDate`:
      - If `parsedDate < DateTime.Today`, set `parsedDate = parsedDate.AddYears(1)`.
- [ ] Alternatively, apply this logic centrally in `TradeLegViewModel.ApplyFromOvmlLeg()`
      or `TradeLegViewModel.ApplyExpiryInput()` so the rule is always enforced
      regardless of which parser is used.
- [ ] Log or surface a status hint, e.g. "Expiry rolled to next year ({date})"
      in `StatusMessage`.
- [ ] Add unit tests covering: past date → next year, today → unchanged,
      future date → unchanged, leap-year edge case (Feb 29).

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
- [ ] Update integration tests / existing unit tests to cover the new flows above.