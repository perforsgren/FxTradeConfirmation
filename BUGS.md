MEDEL
M1. OvmlBuilderAP3 ExtractPair matchar alla 6-bokstavsord utan allowlist — OvmlBuilderAP3.cs
"London", "strike", "option" matchar [A-Za-z]{6} som valutapar om _knownPairs är tom (vid startup innan DB-laddning).

M2. \b i CleanInput matchar inte ! korrekt — OvmlBuilderAP3.cs
Regex.Escape("!") → \!, och \b\!\b matchar aldrig since ! inte är ett word-tecken. Dead code i loop, men [^\w\s\.\,] fångar det ändå.

M3. Ingen maxgräns på antal legs från regex — OvmlBuilderAP3.cs
Pathologisk input med hundratals "buy"/"sell" skapar hundratals OvmlLeg-objekt.

M4. ExtractExpiryOrTenor använder DateTime.Now.Year — OvmlBuilderAP3.cs
Lokal tid. Runt midnatt/nyår kan fel år infereras. Saknar year-rollover om månaden är i dåtid.

M5. double→decimal konversion i BloombergFx.cs
Convert.ToDecimal(fieldData.GetElementAsFloat64(...)) — IEEE 754 double kan inte representera alla decimaltal exakt. FX-kurser kan få avrundningsfel.

M6. Fallback BID/ASK logg är missvisande — BloombergFx.cs
När bid==null && ask==null, loggar koden "returning ASK=null" istället för "neither side available".

M7. _isSyncingUserProfile utan exception-recovery — TradeLegViewModel.cs
Om OnSalesChanged kastar efter _isSyncingUserProfile = true, förblir flaggan true och Sales↔InvestmentDecisionID-sync blockas permanent.

M8. SourceFilter/WindowTitleFilter kan sättas till null → NRE — ClipboardWatcher.cs
Public setters utan null-guard. .Count anropas utan kontroll.

M9. SuppressClipboardEvents callback utanför Dispatcher — BloombergPaster.cs
SuppressClipboardEvents?.Invoke(false) i finally-blocket körs efter Task.Delay, potentiellt på thread pool → cross-thread access.

M10. InputUnion explicit Size=32 kan vara fel på x64 — BloombergPaster.cs
Win32 INPUT-unionen på 64-bit Windows är 40 bytes. Hardkodat 32 kan orsaka out-of-bounds.

M11. _cachedTerminalHwnd race condition — BloombergPaster.cs
Static fält utan synkronisering. Compound read-validate-write är inte atomärt.

M12. OnBrokerChanged/OnMicChanged potentiell infinite recursion — TradeLegViewModel.cs
Terminerar bara om ObservableProperty equality-check stoppar cykeln. Inkonsekvent mapping → StackOverflow.

M13. Legs enumeras på UI-tråd utan explicit tråd-affinitet — MainViewModel.cs
ObservableCollection utan lås. Om bakgrundskod någonsin modifierar Legs → InvalidOperationException.

M14. PopulateLegsFromParsed förlorar admin-defaults vid rebuild — MainViewModel.cs
Nya legs ärver inte Trader/Sales/BookCalypso som sattes i SetupUserDefaults.

M15. API-nyckel i klartext på disk — OvmlBuilder.cs
Key.txt bredvid prompt-filen. Bör vara i credential manager.

M16. NotionalParser CountInputDecimals inkonsekvent med Parse — NotionalParser.cs
Duplicerad logik med skillnader: CountInputDecimals("1,500") returnerar 3 decimaler medan Parse("1,500") returnerar 1500. Inkonsekvent beteende.

M17. TradeLeg.Trader hårdkodat default "P901PEF" — TradeLeg.cs
Specifik persons ID som default. Om personen slutar → stale default.

M18. TradeLeg.MIC default "XOFF" — TradeLeg.cs
Default off-exchange kan vara regulatorisk felrapportering.

M19. ExpiryDateParser fallback DateTime.TryParse med InvariantCulture — ExpiryDateParser.cs
"03/04/2025" tolkas som 4 mars. Europiska användare som förväntar dd/MM får fel datum.

M20. ExpiryDateParser "29/2" på icke-skottår — ExpiryDateParser.cs
new DateTime(DateTime.Today.Year, 2, 29) kastar, fångas av catch → null. AddYears(1) hittar inte nästa skottår.

M21. MicBrokerMapping case-sensitive nycklar — MicBrokerMapping.cs
"bgco" matchar inte "BGCO". Returnerar tyst "XOFF"/tom sträng.

M22. MicBrokerMapping: null input → ArgumentNullException — MicBrokerMapping.cs
Inga null-checks. Dictionary.TryGetValue(null, ...) kastar.

M23. Enums utan explicita integer-värden — Enums.cs
Om ordning ändras och enums serialiserats som int → tyst förändrad semantik. Särskilt PremiumStyle som styr beräkningar.

M24. Stale lock TOCTOU — RecentTradeService.cs
TryBreakStaleLock kollar ålder och sedan raderar — klassisk time-of-check/time-of-use race.

M25. LoadFromModel: PremiumStyle sätts före Premium — TradeLegViewModel.cs
Triggers OnPremiumStyleChanged → RecalculatePremiumFromPremiumText innan PremiumText satts. Ordningsberoende.

M26. Owner.Width/Height kan vara NaN — ClipboardCaptureDialog.xaml.cs
WPF SizeToContent-fönster har Width=NaN. (NaN - ActualWidth) / 2 → NaN → dialog försvinner off-screen.

M27. App.xaml.cs: OvmlBuilder/OptionQueryFilter konstruktorer inte try/catch-skyddade — App.xaml.cs
OptionQueryFilter och OvmlBuilder konstruktorer kan kasta vid startup om nätverkssökväg är oåtkomlig.

LÅGT / SMÅSAKER
L1. ClipboardCaptureDialog.Header_MouseLeftButtonDown är tomt — ClipboardCaptureDialog.xaml.cs
Tomt event med "intentionally empty" kommentar. Borde ta bort XAML-eventet istället.

L2. OvmlBuilder promptfil läses vid varje parse — OvmlBuilder.cs
Ingen cachning. Under hög frekvens = onödigt disk-I/O.

L3. OptionQueryFilter: ingen file watcher — OptionQueryFilter.cs
Regler uppdateras bara vid restart.

L4. using System.Security.Cryptography oanvänd — TradeLegViewModel.cs
Dead import.

L5. IsWindowVisible deklarerad men aldrig anropad — BloombergPaster.cs
Dead P/Invoke.

L6. moveBusinessDays saknar MaxRollDays-skydd — DateConvention.cs
Alla andra loop-metoder har det, men denna verifierades aldrig.

L7. ctryCurrency/ctryCalender är parallella arrayer — DateConvention.cs
Fragilt mönster. "Calender" är dessutom felstavat ("Calendar").

L8. DateConvention: DataTable allokeras i fält-init, sedan skrivs över i konstruktorn — DateConvention.cs
Bortkastade new DataTable()-allokering.

L9. ToLowerInvariant() på varje filter-element vid varje clipboard-event — ClipboardWatcher.cs
Borde använda StringComparison.OrdinalIgnoreCase istället.

L10. ~20 hardkodade hex-färger i ClipboardCaptureDialog.xaml
Borde komma från theme resources.

L11. Ingen InputBindings (Ctrl+S, Ctrl+N etc.) i MainWindow.xaml
Trading-app utan keyboard shortcuts.

L12. Inga AutomationProperties i hela UI:t
Helt otillgängligt för screen readers.

L13. Inga FallbackValue/TargetNullValue på bindings genom hela XAML
Null properties ger tomma/osynliga element utan indikation.

L14. Duplicerade ControlTemplates i ClipboardCaptureDialog.xaml
~120 rader identisk template för 4 knappar. Borde vara en delad Style.

L15. Bloomberg watcher toggle i MainWindow.xaml är StackPanel med MouseLeftButtonUp
Inte keyboard-fokusbar/tillgänglig. Borde vara ToggleButton.

L16. RecentTradeEntry.SavedDate sätts vid konstruktion, inte vid sparande
Deserialisering utan SavedDate-fält → tidsstämpel blir deserialiseringstid.

L17. CallApiAsync hårdkodar svensk text — OvmlBuilder.cs
"(Idag ar {today}.) — locale-antagande i AI-prompten.

L18. PremiumCalculator: ingen avrundning
Beräknade premium kan ha 28+ siffrors precision. Bör avrundas till lämpligt antal decimaler.

L19. getForwardDate är public men övriga metoder är private — DateConvention.cs
Inkonsekvent API-yta, sannolikt oavsiktligt.

L20. OvmlBuilderAP3: \w kanske inte matchar ö/ä i CleanInput — OvmlBuilderAP3.cs
Om .NET regex-motor inte matchar svenska tecken med \w, strippas de → svenska nyckelord "köpa"/"sälja" matchar aldrig i RxLeg.