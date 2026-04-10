
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

## 2. DateTimePicker for Expiry Date in ClipboardCaptureDialog ✅

**Goal:** Replace the plain read-only expiry text label in `ClipboardCaptureDialog`
with an interactive `DatePicker` (or a third-party `DateTimePicker`) so the user
can correct a parsed expiry date before clicking "Fill Form".

**Tasks:**
- [x] Add a `DatePicker` (WPF built-in) or a suitable third-party control
      (e.g. `Xceed.Wpf.Toolkit.DateTimePicker`) to the dialog XAML in place of
      `ExpiryLabel`.
      → Implemented as a custom `Calendar`-in-`Popup` picker built entirely in
        code-behind (`BuildHeaderPicker()`). `ExpiryLabel` becomes a clickable
        link (hand cursor, hover underline) that opens a dark-themed calendar
        popup; no third-party dependency needed.
- [x] Bind the picker to a `SelectedDate` property on the dialog or on each
      `LegRow`, defaulting to the parsed expiry date.
      → `LegRow.ExpiryDate` (`DateTime?`) holds the date, parsed from
        `OvmlLeg.Expiry` in the `LegRow` constructor. The header calendar's
        `SelectedDate` is initialised from `ParsedLegs[0].ExpiryDate`.
- [x] When the dialog is multi-leg and expiry dates differ across legs, show a
      per-leg date picker in the legs list instead of a single header picker.
      → `_mixedExpiry` flag (distinct expiry count > 1) controls visibility of
        `LegExpiryList`. `BuildLegPickers()` wires a separate `Calendar` popup
        to the `LegExpiryLabel` in each leg row; `ExpiryStack` hosts all popups.
- [x] Propagate the (potentially user-edited) expiry date through `LegRow` →
      `ToOvmlLeg()` so that `RebuildOvmlFromCurrentLegs()` uses the corrected date.
      → `LegRow.ToOvmlLeg(original)` overrides `Expiry` with
        `_expiryDate.Value.ToString("yyyy-MM-dd")` when a date is set.
        `RebuildOvmlFromCurrentLegs()` calls `ToOvmlLeg` for every row and
        feeds the result to `OvmlBuilderAP3.RebuildOvml()`.
- [x] Ensure the corrected date is also forwarded to `TradeLegViewModel.ExpiryDate`
      when the user confirms with "Fill Form" or "Both".
      → `ParsedLegs` (with updated `ExpiryDate`) are exposed via
        `ClipboardCaptureDialog.ParsedLegs`; `MainViewModel.PopulateLegsFromParsed`
        calls each leg's `ToOvmlLeg()` before passing it to
        `TradeLegViewModel.ApplyFromOvmlLeg()`, which calls `ApplyExpiryInput(leg.Expiry)`
        with the corrected date string.

---

## 3. Weekend & Holiday Warning on Expiry Date ✅

**Goal:** After the user clicks "Fill Form" (or the equivalent confirm action),
detect if the resolved expiry date falls on a weekend or a local market holiday
and ask whether to roll the date backward, forward, or keep it (non-bookable).

**Tasks:**
- [x] **Weekend check:** After the user confirms in `ClipboardCaptureDialog`,
      inspect each leg's expiry date. If it lands on Saturday or Sunday, show a
      modal dialog asking the user to roll or keep the date.
      → `MainWindow.OnClipboardCaptureDialogRequested` calls `PromptWeekendRoll(vm)`
        after `PopulateLegsFromParsed` for both `PopulateUi` and `Both` results.
        Iterates all legs via `TradeGridControl.PromptWeekendRoll(leg)`.
- [x] Roll dialog with Previous / Next / Keep choices.
      → `WeekendRollDialog` (`Views\WeekendRollDialog.xaml` + `.cs`) — dark-themed
        modal with amber warning header, three buttons (◀ Previous / Next ▶ / Keep),
        and Escape key support. `RollAction` enum carries the user's choice.
- [x] Apply the chosen roll direction to `TradeLegViewModel.ExpiryDate`.
      → `PromptWeekendRoll(TradeLegViewModel)` in both `MainWindow.cs` and
        `TradeGridControl.cs` walks Saturday/Sunday days forward or backward until
        a weekday is reached, then calls `leg.ApplyExpiryDateDirect(rolledDate)`.
- [x] **Holiday check:** Leverage the existing `Holidays` `DataTable` already
      loaded in `MainViewModel` (columns `Market` / `HolidayDate`) to determine
      whether the expiry date is a holiday for the relevant currency-pair markets.
      → `TradeLegViewModel.ValidateExpiryDate()` calls `Calendar.IsMarketHoliday()`
        with the markets derived from `DateConvention.ctryNames(CurrencyPair)`.
- [x] Apply visual warnings inside `TradeLegViewModel` via `ValidateExpiryDate()`:
      - `[ObservableProperty] bool ExpiryIsWeekend` — true when expiry falls on
        Saturday or Sunday.
      - `[ObservableProperty] bool ExpiryIsHoliday` — true when expiry falls on a
        local market holiday (but not a weekend).
      - `[ObservableProperty] string ExpiryWarningTooltip` — human-readable
        description of the weekend day or matching holiday market(s).
- [x] **Weekend → red border** on expiry cell in the grid.
      → `TradeGridControl.BuildOptionRows` wires a `PropertyChanged` listener on
        each leg. `UpdateExpiryVisuals()` sets `BorderBrush = NegativeRedBrush`
        and `BorderThickness = 2` when `ExpiryIsWeekend` is true.
- [x] **Holiday → red font** on expiry cell in the grid.
      → Same `UpdateExpiryVisuals()` sets `Foreground = NegativeRedBrush` when
        `ExpiryIsHoliday` is true. Border and thickness revert to defaults.
- [x] Attach a tooltip with the holiday description when `ExpiryIsHoliday` is true.
      → `expiryTb.ToolTip` is set from `leg.ExpiryWarningTooltip` by
        `UpdateExpiryVisuals()`; falls back to a generic message when the tooltip
        string is empty.
- [x] **Auto-clear:** Both flags and their visual formatting are cleared
      automatically when the user edits the expiry date to a valid business day.
      → `OnExpiryDateChanged` calls `ValidateExpiryDate()` on every change.
        `ApplyExpiryInput` and `ApplyExpiryDateDirect` also call it explicitly so
        re-entering the same date still refreshes the flags.
- [x] `Calendar.IsMarketHoliday(DateTime date, DataTable holidays, IEnumerable<string> markets)`
      helper added to `Helpers\Calendar.cs`.
      → Static method returning `(bool isHoliday, string description)`. Checks for
        an optional `HolidayName` column and includes the name in the description
        when present — no DB schema change required.
- [x] Weekend roll also wired to direct grid entry (user types a weekend date
      into the expiry cell without going through the clipboard dialog).
      → `leg.WeekendExpiryDetected` event on `TradeLegViewModel` is subscribed in
        `TradeGridControl.BuildOptionRows` via `leg.WeekendExpiryDetected += PromptWeekendRoll`.
        `ApplyExpiryInput` raises the event when `ExpiryIsWeekend` is true after
        parsing.

**Note:** `HolidayName` column is read opportunistically — `Calendar.IsMarketHoliday`
checks `holidays.Columns.Contains("HolidayName")` at runtime. Extending the AHS
SQL query in `DatabaseService.LoadHolidaysAsync()` to include `HOLIDAY_NAME` will
automatically surface richer descriptions without any further code changes.

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

## 7. Add Actions Dropdown with "Send to OVML" ✅

**Goal:** Add an **Actions** dropdown button to the toolbar with secondary actions
and a new "Send to OVML" entry that generates OVML syntax from all current legs in
the UI and pastes it to the Bloomberg Terminal.

**Implementation note:** The originally planned `<Menu>` element ersattes av ett
`Button + ContextMenu`-mönster (drop-down button). Detta ger full kontroll över
mörk-temat — `ContextMenu` och `MenuItem` stylas via `DarkContextMenu`,
`DarkMenuItem` och `DarkMenuSeparator` i `DarkBlueTheme.xaml` med befintliga
brush-resurser. Ingen tredjepartsberoende krävs.

**Tasks:**

### 7a. Toolbar restructure
- [x] **Add Leg**, **Clear All** och **Save** finns kvar i toolbaren på sina
      ursprungliga platser — dessa är de vanligaste åtgärderna.
- [x] **Open Recent**, **Send Mail** och **Toggle Details** flyttade in i
      **Actions**-dropdownen, vilket rensar toolbaren.
- [x] `ActionsButton` (`ToolbarDropdownButton`-stil) placerad mellan **Clear All**
      och höger-justerade **Save**. Visar hamburger-ikon + "Actions"-etikett + pil.
- [x] `OnActionsDropdownClick`-handler i `MainWindow.xaml.cs` öppnar `ContextMenu`
      programmatiskt vid vänsterklick (`menu.IsOpen = true`), eftersom WPF annars
      bara visar `ContextMenu` vid högerklick.

### 7b. Dark theme styles tillagda i `DarkBlueTheme.xaml`
- [x] `ToolbarDropdownButton` — ärver `ToolbarButton`, justerar höger-padding för pilen.
- [x] `DarkContextMenu` — `BgCardBrush`-bakgrund, `BorderDefaultBrush`-kant,
      `CornerRadius="6"`, droppskugga, `StackPanel` items host.
- [x] `DarkMenuItem` — transparent bakgrund, `BgHoverBrush` vid hover,
      `TextDimmedBrush`-förgrund + `Arrow`-cursor när disabled.
- [x] `DarkMenuSeparator` — 1 px `BorderSubtleBrush`-linje med 8 px sidmarginaler.

### 7c. "Send to OVML"-kommando
- [x] `[RelayCommand] private async Task SendAllToBloombergAsync()` tillagd i
      `MainViewModel`.
- [x] Guardar: `_bloombergPaster == null` → statusvarning + early return;
      `Legs.Count == 0` → statusvarning + early return.
- [x] Ingen fältnivå-validering — `CurrencyPair`, `BuySell` och `CallPut` har
      alltid giltiga defaultvärden på varje leg.
- [x] `TradeLegViewModel.ToOvmlLeg()` tillagd — konverterar aktuellt UI-tillstånd
      till ett `OvmlLeg`-record. Bevarar delta-strike-text (t.ex. `"25D"`),
      formaterar expiry som `yyyy-MM-dd` för `RebuildOvml`, utelämnar spot
      (lagras inte per leg i UI:t).
- [x] `OvmlBuilderAP3.RebuildOvml(ovmlLegs)` anropas för att producera den
      kombinerade OVML-strängen — multi-leg-medveten, delad notional kollapsas
      till en `N`-token.
- [x] `OvmlBuilderAP3.BuildOvml` uppdaterad: `N`-token emitteras bara när minst
      en leg har `Notional > 0` — undviker `N0M` i outputen när notional är tom.
- [x] `SendToBloombergAsync(combinedOvml)` återanvänds för det faktiska
      clipboard-paste-flödet (suppress → paste → restore, identiskt med
      clipboard-capture-flödet).
- [x] `IsBloombergAvailable` (`bool`-property) exponerad på `MainViewModel` —
      bunden till `IsEnabled` på "Send to OVML"-`MenuItem` så att den gryas ut
      automatiskt när `_bloombergPaster` är `null`.

---

## General / Cross-Cutting

- [ ] Review all new user-facing strings for consistent English phrasing and
      alignment with existing status message style (✓ / ⚠ / ⏳ prefix convention).
- [ ] Ensure all new `async` commands use `.FireAndForget(onError: ...)` or
      proper `try/catch` so exceptions do not silently escape.
- [ ] Add XML doc comments to all new public methods and properties.

---

## 8. Drag-and-Drop Leg Reordering

**Goal:** Låt användaren dra och släppa legs i `TradeGridControl` för att ändra
ordning — t.ex. flytta Leg 3 till position 1 utan att manuellt kopiera fält.

**Bakgrund och analys:**
`MainViewModel.Legs` är en `ObservableCollection<TradeLegViewModel>`. Varje leg
renderas som en rad i ett dynamiskt byggt `Grid` i `TradeGridControl.BuildOptionRows()`.
WPF:s inbyggda drag-and-drop (`DragDrop.DoDragDrop`) kan användas utan externa
beroenden. Komplexiteten ligger i att:
1. Varje rad är ett gäng lösa `UIElement`s i ett `Grid` (inte ett `ListBox`/
   `DataGrid`) — det finns ingen inbyggd drag-source per rad.
2. `LegNumber` (1-baserat) används på flera ställen för logik (`IsFirstLeg`,
   `PropagateFromLeg1`) och måste räknas om efter en flytt.
3. `CurrencyPair` och `Counterpart` propageras från Leg 1 till alla övriga —
   detta invariant måste hållas efter reorder.

**Tasks:**
- [ ] Lägg till en drag-handle-kolumn längst till vänster i
      `TradeGridControl.BuildOptionRows()` — ett litet `☰`-ikon-element per rad
      med `Cursor = Cursors.SizeNS` och en `MouseMove`-handler som startar
      `DragDrop.DoDragDrop(leg, legViewModel, DragDropEffects.Move)`.
- [ ] Prenumerera på `DragOver` och `Drop` på hela rad-containern (eller på
      `TradeGridControl` själv). I `Drop`-handleren: räkna ut målindex från
      musposition, anropa `MainViewModel.MoveLeg(fromIndex, toIndex)`.
- [ ] Implementera `MainViewModel.MoveLeg(int from, int to)`:
      - Flytta elementet i `Legs`-kollektionen.
      - Räkna om `LegNumber` för alla legs (`for (int i = 0; i < Legs.Count; i++) Legs[i].LegNumber = i + 1`).
      - Re-trigga `PropagateFromLeg1` för `CurrencyPair` och `Counterpart` så
        att de nya Leg 2..N ärver från den nya Leg 1.
      - Anropa `NotifyLegChanged()` och `UpdateSaveValidation()`.
- [ ] Visa en visuell drop-indikator (horisontell linje) mellan raderna under
      drag — rita den i `DragOver`-handleren med ett overlay-element i
      `TradeGridControl`.
- [ ] Eftersom det dynamiska `Grid`-layouten byggs om vid `BuildOptionRows()",
      räcker det troligen att anropa `RebuildGrid()` (eller motsvarande) efter
      `MoveLeg` — rader ritas om i ny ordning automatiskt.
- [ ] Testa edge cases: 1 leg (inget drag behövs), drag av Leg 1 (ny Leg 1 måste
      propagera pair/counterpart), drag under pågående solve (blockera eller
      ignorera).

**Prioritet:** Låg — komfortförbättring. Inga blockers mot övrig funktionalitet.

---

## 9. Auto-Save Draft ✅

**Goal:** Spara formulärtillståndet automatiskt var N:e minut till en temporär
JSON-fil. Om appen kraschar kan användaren välja att återställa senaste draft
vid nästa uppstart.

**Bakgrund och analys:**
`TradeLegViewModel.ToModel()` → `TradeLeg` finns redan och täcker alla fält.
`MainViewModel` har redan `SaveAsync()` och `LoadRecentAsync()` via
`DatabaseService`. Draft-sparning behöver **inte** gå mot databasen — en lokal
JSON-fil i `%LOCALAPPDATA%\FxTradeConfirmation\draft.json` (eller `%TEMP%`) är
billigare, snabbare och kräver inga SQL-ändringar. `System.Text.Json` finns
tillgängligt i .NET 8 utan extra paket.

**Tasks:**
- [x] Skapa `Services\DraftService.cs` med tre metoder:
      - `SaveDraftAsync(IEnumerable<TradeLeg> legs, string counterpart)` —
        serialiserar till JSON och skriver till draft-filen med `FileStream`
        (`FileMode.Create`, `FileShare.None`).
        → Skriver till `%LOCALAPPDATA%\FxTradeConfirmation\draft.json`.
          Katalogen skapas automatiskt med `Directory.CreateDirectory`.
      - `TryLoadDraft(out DraftData? draft)` — läser och deserialiserar filen;
        returnerar `false` om filen inte finns eller är korrupt.
      - `DeleteDraft()` — tar bort filen efter en lyckad `SaveAsync` eller
        manuell clear, så man inte störs av föråldrade drafts.
- [x] `DraftData`-record: `DateTime SavedAt`, `string Counterpart`,
      `List<TradeLeg> Legs` — i `Models\DraftData.cs`.
- [x] `DraftService.SaveDraftAsync` är defensiv mot concurrent writes via
      `SemaphoreSlim(1,1)` — timer-tick och stängnings-save kan inte överlappa.
- [x] I `MainViewModel`: `DispatcherTimer` med intervall 3 minuter (konstant
      `AutoSaveIntervalMinutes`). På varje tick anropas
      `DraftService.SaveDraftAsync(Legs.Select(l => l.ToModel()), ...)`.
      → Timer startas i `InitializeAsync()` via `StartAutoSaveTimer()`.
      → `OnAutoSaveTick` är en namngivet handler (undviker `async void` på
        anonymous lambda).
- [x] Auto-save hoppas över när `Legs.Count == 0` — ingen tom draft sparas.
- [x] I `MainWindow.OnWindowClosing` (registrerad på `Closing`-eventet):
      `SaveDraftAsync` anropas med `await` vid fönsterstängning så att draften
      alltid sparas oavsett om timern hann ticka — täcker stängning inom 3 min.
      → Ersätter de två gamla `Closing += (_, _) => ...`-lambdorna med en
        namngiven `async void OnWindowClosing` handler.
- [x] Vid uppstart i `MainViewModel.InitializeAsync()`: anropas `TryRestoreDraft()`.
      → Om en draft finns **och** `draft.SavedAt.Date == DateTime.Today`:
        visas `Views\RestoreDraftDialog` (dark-themed, samma stil som
        `WeekendRollDialog`). Ja → `LoadLegsFromModels(draft.Legs)`.
        Nej → `DraftService.DeleteDraft()`.
      → Om draften är från ett tidigare datum: tas bort tyst utan dialog.
- [x] `LoadLegsFromModels(IReadOnlyList<TradeLeg>)` — ny privat hjälpmetod i
      `MainViewModel` som speglar `LoadTrade`-flödet:
      `CancelSolving` → `Legs.Clear()` → `LoadFromModel` per leg →
      `RenumberLegs` → `UpdateTotalPremium` → `UpdateSaveValidation`.
- [x] Draft raderas automatiskt vid:
      - Lyckad `SaveAsync` (alla legs sparade utan fel).
      - `ClearAll()` (användaren rensar formuläret manuellt).
- [x] `DraftService` exponeras som `public DraftService DraftService { get; }`
      på `MainViewModel` så att `MainWindow.OnWindowClosing` kan anropa
      `SaveDraftAsync` direkt utan extra event/indirektion.
- [x] `Views\RestoreDraftDialog.xaml` + `.xaml.cs`:
      - `WindowStyle="None"`, `AllowsTransparency="True"`, `BgCardBrush`-border,
        `AccentBlueBrush`-kant — identisk chrome som övriga dialoger.
      - Visar `"Saved at HH:mm"` (datum utelämnas — draften är alltid från idag).
      - Knappar: **↺ Restore** (Enter) / **Discard** (Esc) — båda med
        `TradingButton`-stil, ingen `Foreground`-override på Discard-knappen.
      - `ShouldRestore`-property avläses av `TryRestoreDraft()`.

**Prioritet:** Medel — billig försäkring, liten implementation, stor nytta vid
sällsynta krascher.

---

## 10. Manuell Ctrl+V-inklistring (utan Clipboard Monitor) ✅

**Goal:** Låt användaren inaktivera `ClipboardWatcher` och istället manuellt
klistra in Bloomberg-text via `Ctrl+V` direkt i huvudfönstret — samma
parse/fill-flöde som idag men utan att appen lyssnar passivt på urklipp.

**Bakgrund och analys:**
`ClipboardWatcher` prenumererar idag på `WM_CLIPBOARDUPDATE` via ett dolt
`HwndSource`-fönster. Flödet triggas alltså automatiskt när Bloomberg-text
kopieras. Vissa traders föredrar manuell kontroll — t.ex. för att undvika
att privata urklipp råkar trigga appen. Lösningen är:
1. En toggle i UI:t (t.ex. en `ToggleButton` i toolbaren eller en inställning)
   som pausa/återupptar `ClipboardWatcher`.
2. Ett `PreviewKeyDown`-hook på `MainWindow` som fångar `Ctrl+V` när
   watchern är pausad och matar in urklippstexten manuellt i
   `RunClipboardFlowAsync`.

**Tasks:**
- [x] Lägg till `ClipboardWatcher.IsEnabled` (`bool`-property med en
      `_suppressAll`-flag) — när `false`, ignoreras alla `WM_CLIPBOARDUPDATE`
      anrop och `ClipboardChanged`-eventet höjs inte.
      → `volatile bool _suppressAll` i `ClipboardWatcher`. `WndProc` short-
        circulerar när flaggan är satt — `HwndSource`-hooken förblir registrerad
        så att re-enable är omedelbart utan Stop/Start-cykel.
- [x] Exponera `[ObservableProperty] bool ClipboardMonitorEnabled` på
      `MainViewModel`, bunden till `ClipboardWatcher.IsEnabled`. Default: `true`.
      Persist värdet i `AppSettings` / `Properties.Settings` så det överlever
      omstarter.
      → `partial void OnClipboardMonitorEnabledChanged` synkar `IsEnabled`,
        uppdaterar statusbar och anropar `AppSettings.Save()`. Värdet laddas
        från `AppSettings.Load()` i konstruktorn och synkas till watchern
        innan `ClipboardChanged`-eventet kopplas.
      → `Services\AppSettings.cs` — ny fil, JSON-backad via `System.Text.Json`,
        samma `%APPDATA%\FxTradeConfirmation\`-mapp som `MainWindowPos.json`.
- [x] Lägg till en `ToggleButton` i toolbaren (eller under Actions-dropdownen)
      med label `"📋 Monitor On/Off"` och `IsChecked` bunden till
      `ClipboardMonitorEnabled`.
      → Befintlig statusbar-toggle (`OnClipboardWatcherToggleClick`) binder nu
        till `ClipboardMonitorEnabled` istället för `ClipboardAutoEnabled`.
        Ellipse och label-text styrs via `DataTrigger` på `ClipboardMonitorEnabled`.
- [x] I `MainWindow.xaml.cs`, prenumerera på `PreviewKeyDown`:
      → `PreviewKeyDown += OnPreviewKeyDown` i konstruktorn. Handleren
        kontrollerar `Key.V + ModifierKeys.Control + !ClipboardMonitorEnabled`,
        läser `Clipboard.GetText()` och anropar `vm.RunClipboardFlowAsync`
        via `Task.Run`. `e.Handled = true` förhindrar att Ctrl+V bubblar vidare.
        `RunClipboardFlowAsync` vidgades till `internal` för åtkomst från vyn.
- [x] Uppdatera statusbar-texten vid toggle:
      `"📋 Clipboard monitor pausad — klistra in med Ctrl+V"` resp.
      `"📋 Clipboard monitor aktiv"`.
      → Sätts i `OnClipboardMonitorEnabledChanged` på `MainViewModel`.
        Statusbar-label i `MainWindow.xaml` uppdaterad:
        `"MONITOR PAUSAD — Ctrl+V"` / `"BLOOMBERG WATCHER ON"`.
- [x] Se till att `_suppressClipboardEvents`-flaggan i `MainViewModel`
      inte kolliderar med den nya `IsEnabled`-flaggan i `ClipboardWatcher`
      (de är ortogonala — suppress gäller enskilda skrivningar, IsEnabled
      gäller lyssnandet generellt).
      → Dokumenterat i `IClipboardWatcher.IsEnabled` XML-doc och i
        `OnClipboardMonitorEnabledChanged`-kommentaren. `OnClipboardChanged`
        har ett dubbelt guard (`_suppressClipboardEvents || !ClipboardMonitorEnabled`)
        som skyddar mot race-conditions där ett in-flight event redan är köat
        på dispatchern när flaggan ändras.

**Prioritet:** Medel — efterfrågat av traders som är försiktiga med passiv
clipboard-access.

---

## 11. Straddle / Strangle / Risk Reversal-Templates

**Goal:** Låt användaren snabbt skapa vanliga multi-leg-strukturer via
Actions-dropdownen — t.ex. "Ny 25D Risk Reversal EURSEK 3M" → 2 legs
pre-populerade med rätt `BuySell`, `CallPut`, `Strike`-text och delad expiry.

**Bakgrund och analys:**
Strukturerna är väldefinierade:

| Struktur | Leg 1 | Leg 2 |
|---|---|---|
| **Straddle** | Buy Call ATM | Buy Put ATM |
| **Strangle** | Buy Call 25D | Buy Put 25D |
| **Risk Reversal** | Buy Call 25D | Sell Put 25D |

`MainViewModel.AddLeg()` finns redan. `TradeLegViewModel.ApplyFromOvmlLeg()`
kan återanvändas för att sätta fält. Ingen ny parsning behövs — värdena är
hårdkodade per strukturtyp. Templaten behöver bara sätta:
`BuySell`, `CallPut`, `StrikeText` (`"ATM"` eller `"25D"`), `CurrencyPair` och
`ExpiryText` (om användaren anger tenor i dialogen).

**Tasks:**
- [ ] Skapa `Models\StructureTemplate.cs`:
      ```csharp
      record StructureLeg(BuySell BuySell, CallPut CallPut, string StrikeText);
      record StructureTemplate(string Name, IReadOnlyList<StructureLeg> Legs);
      ```
      Definiera ett statiskt `StructureTemplate.All`-fält med de tre
      standardstrukturerna (Straddle, Strangle, 25D RR). Fler kan enkelt läggas
      till.
- [ ] Lägg till ett undermeny-alternativ `"📐 New Structure…"` i
      `ActionsButton`-menyn i `MainWindow.xaml`. Alternativt: en flyout med
      direktval per struktur (`"Straddle"`, `"Strangle"`, `"25D RR"`).
- [ ] Skapa `Views\NewStructureDialog.xaml` — en liten modal:
      - `ComboBox` med struktur-valen.
      - `TextBox` för tenor/expiry (förval `"3M"`).
      - `ComboBox` för currency pair (förval = aktuell pair om legs finns,
        annars `"EURSEK"`).
      - OK / Cancel-knappar.
- [ ] I `MainViewModel`: `[RelayCommand] async Task NewStructureAsync()` —
      öppnar `NewStructureDialog`, och vid OK:
      1. `ClearAll()` (om `Legs.Count > 0` och användaren bekräftar).
      2. Iterera `template.Legs`, anropa `AddLeg()` för varje leg, och sätt
         `BuySell`, `CallPut`, `StrikeText` direkt på det nya
         `TradeLegViewModel`.
      3. Anropa `ApplyExpiryInput(tenor)` på varje leg.
      4. `SetStatusAsync("✓ Struktur skapad: {template.Name} {pair} {tenor}")`.
- [ ] `Counterpart` och `NotionalText` lämnas tomma — traderns uppgift att fylla i
      dem, precis som vid manuell inmatning.
- [ ] **Extensibility:** `StructureTemplate.All` bör på sikt kunna läsas från en
      konfigurationsfil (t.ex. `structures.json` i `%APPDATA%`) så att nya
      strukturer kan läggas till utan kodändring. Implementera som en enkel
      JSON-lista med `StructureLeg`-records i en framtida iteration.

**Prioritet:** Medel — sparar tid vid vanliga strukturer, liten kodbas,
implementeras naturligt ovanpå befintlig Actions-meny.

---

## 12. P&L Payoff-Diagram ✅

**Goal:** Visa ett payoff-diagram vid expiry för hela strukturen (alla legs)
direkt i appen — ett litet WPF-renderat diagram som uppdateras live när
användaren justerar strikes, notional och BuySell.

**Bakgrund och analys:**
WPF:s `StreamGeometry` används för polyline-diagrammet utan tredjepartsbibliotek.
Payoff-beräkningen per vanilla option:
- **Long Call:** `max(S - K, 0) × Notional - PremiumAmount`
- **Long Put:** `max(K - S, 0) × Notional - PremiumAmount`
- **Short:** negera.
- **Summera** alla legs vid varje spot-punkt.

`PremiumAmount` (total cashbelopp) används istället för `Premium` (pips/pct-kurs)
så att premium-kostnaden är på samma skala som payoff-beloppet.

**Tasks:**
- [x] Skapa `Helpers\PayoffCalculator.cs`:
      → `static class PayoffCalculator` med tre publika metoder:
        - `Calculate(legs, minSpot, maxSpot, points = 200)` — returnerar
          `IReadOnlyList<(double Spot, double PnL)>`. Itererar 200 jämnt fördelade
          spot-värden. Per leg: `intrinsic × notional ± PremiumAmount`. Legs med
          icke-numeriska strikes (t.ex. `"25D"`) eller utan notional hoppas över.
        - `GetSpotRange(legs)` — returnerar `(Min, Max)?` med ±15 % buffer kring
          medianen av alla strikes; ger centrerat synfält vid enkla strukturer.
        - `GetXTicks(min, max, targetTicks)` — "nice number"-algoritm (1/2/2.5/5/10
          × 10^n) för jämna tick-värden på båda axlarna.
- [x] Skapa `Views\PayoffChartControl.xaml` + `PayoffChartControl.xaml.cs`:
      → `UserControl` (Height="240") med ett `Canvas` (`ClipToBounds="True"`) som
        enda barn. All rendering sker i code-behind via `StreamGeometry` och
        WPF-shapes — inget tredjepartsbibliotek.
      → Prenumererar på `MainViewModel.Legs.CollectionChanged` och på
        `PropertyChanged` för `StrikeText`, `BuySell`, `CallPut`, `NotionalText`,
        `PremiumText`, `PremiumAmountText` på varje leg.
      → `ScheduleRedraw()` defer:ar med `DispatcherPriority.Loaded` för att
        garantera att `ActualWidth`/`ActualHeight` är satta; koalescerar snabba
        ändringar till ett enda ritanrop. Ritar även om vid `IsVisibleChanged`.
      → Ritade element: horisontell nolllinje, Y-axelticks med "nice" labels,
        X-axelticks med vertikala gridlinjer + adaptiv decimalprecision,
        prickade vertikala strikelinjer, halvtransparent profit/loss-shading,
        blå `StreamGeometry`-kurva (StrokeThickness=2), break-even-markeringar
        (amber prickad linje + `"B/E …"`-etikett ovanför plotytan).
      → Tom canvas visar centrerad ledtext när inga numeriska strikes finns.
- [x] Integrera `PayoffChartControl` i `MainWindow.xaml`:
      → Placerad i Row 4 (mellan Total Premium-raden och statusraden).
        Ordning nedifrån: Statusbar (Row 5) → Payoff Chart (Row 4) →
        Total Premium (Row 3) → Trade Grid (Row 2).
      → `Visibility` bunden till `ShowPayoffChart` via `BoolToVis`-converter.
      → `[ObservableProperty] bool ShowPayoffChart` + `[RelayCommand] TogglePayoffChart()`
        tillagda på `MainViewModel`.
      → `"📈 Show Payoff"`-menyalternativ tillagt i `ActionsButton`-menyn.
      → `OnVmPropertyChanged` i `MainWindow.xaml.cs` utökad med
        `nameof(MainViewModel.ShowPayoffChart)` så `FitToContent()` triggas
        vid toggle.
- [x] Hantera edge cases:
      → Inga legs / alla strikes tomma → centrerad ledtext på canvas.
      → Delta-strikes (`"25D"`) utan numeriskt värde → leggen hoppas över i
        `Calculate` och `GetSpotRange`; övriga legs ritas normalt.
      → Blandade `NotionalCurrency` → ingen normalisering (visas as-is);
        beloppsskalan på Y-axeln speglar den dominanta valutan.
      → Tog-av/tog-på Hedge Type ändrar fönsterhöjden via `FitToContent` —
        löst genom att även `ShowPayoffChart`-ändringar triggar `FitToContent`.
      → `Height="240"` på `UserControl` säkerställer korrekt mätning under
        `SizeToContent`-cykeln; `Canvas` har ingen explicit storlek och
        fyller hela `UserControl` via default stretch.
- [x] **Prestanda:** `PayoffCalculator.Calculate` är rent synkron, O(legs × 200).
      Med 4 legs = 800 operationer — omedelbart på UI-tråden.

**Buggar fixade under implementation:**
- Platt kurva med premie ifylld → `Premium` (pips/pct) användes istället för
  `PremiumAmount` (cashbelopp); ersatt med `Math.Abs(PremiumAmount)`.
- Tom yta kvar vid toggle → `Height = 0`-hack i `ShowEmpty()` konfliktade med
  `SizeToContent`-cykeln; löst med `ScheduleRedraw` + `Height="240"` på
  `UserControl` utan manuell höjdmanipulation i code-behind.
- Ingen graf visades → `Canvas.ActualWidth/Height` var 0 vid första `Redraw()`
  p.g.a. att kontrollen inte hade layoutats; löst med `DispatcherPriority.Loaded`-
  defer och omritning vid `IsVisibleChanged`.
- Fel row-ordning → `PayoffChartControl` låg i Row 3 ovanför Total Premium;
  korrigerat till Row 4 (under Total Premium, ovanför statusraden).

**Prioritet:** Låg-medel — visuellt imponerande, men kräver mest implementation
av de fem nya punkterna. Naturlig uppföljning efter att templates (nr 11) är klara
så att man direkt kan se payoff för en Risk Reversal eller Butterfly.