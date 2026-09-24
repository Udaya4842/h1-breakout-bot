using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    public enum ExecutionMode
    {
        SignalOnly,
        Trade
    }

    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class MultiAssetH1BreakoutBot : Robot
    {
        private const string DefaultLabel = "H1-55-MULTI-V1";
        private const LocalStorageScope RiskStorageScope = LocalStorageScope.Type;

        private sealed class InstrumentContext
        {
            public string SymbolName;
            public Symbol Symbol;
            public Bars H1Bars;
            public Bars H4Bars;
            public ExponentialMovingAverage H4Ema50;
            public ExponentialMovingAverage H4Ema200;
            public AverageTrueRange H1Atr;
            public DirectionalMovementSystem H1Dms;
            public DateTime LastSignalTimeUtc = DateTime.MinValue;
            public bool IsCrypto;
            public bool IsUsdSensitive;
        }

        private readonly Dictionary<string, InstrumentContext> _contexts =
            new Dictionary<string, InstrumentContext>(StringComparer.OrdinalIgnoreCase);
        private readonly List<DateTime> _newsTimesUtc = new List<DateTime>();

        private double _initialReferenceEquity;
        private double _dayStartEquity;
        private DateTime _dayUtc;
        private bool _dailyLocked;
        private bool _overallLocked;

        private string InitialEquityKey => $"{StoragePrefix} Initial Equity";
        private string DayEquityKey => $"{StoragePrefix} Day Equity";
        private string DayDateKey => $"{StoragePrefix} Day Date";
        private string DailyLockKey => $"{StoragePrefix} Daily Lock";
        private string OverallLockKey => $"{StoragePrefix} Overall Lock";

        [Parameter("Execution Mode", Group = "Safety", DefaultValue = ExecutionMode.SignalOnly)]
        public ExecutionMode Mode { get; set; }

        [Parameter("Bot Label", Group = "Safety", DefaultValue = DefaultLabel)]
        public string BotLabel { get; set; }

        [Parameter("Symbol Groups", Group = "Markets", DefaultValue = "XAUUSD|GOLD;BTCUSDT|BTCUSD;EURUSD;GBPUSD;USDJPY")]
        public string SymbolGroups { get; set; }

        [Parameter("Allow Crypto Weekend", Group = "Markets", DefaultValue = true)]
        public bool AllowCryptoWeekend { get; set; }

        [Parameter("Risk Per Trade %", Group = "Risk", DefaultValue = 0.25, MinValue = 0.01, MaxValue = 1.0, Step = 0.01)]
        public double RiskPerTradePercent { get; set; }

        [Parameter("Max Aggregate Planned Risk %", Group = "Risk", DefaultValue = 0.75, MinValue = 0.25, MaxValue = 3.0, Step = 0.25)]
        public double MaxAggregatePlannedRiskPercent { get; set; }

        [Parameter("Max Concurrent Bot Positions", Group = "Risk", DefaultValue = 3, MinValue = 1, MaxValue = 10)]
        public int MaxConcurrentPositions { get; set; }

        [Parameter("Max USD-Sensitive Positions", Group = "Risk", DefaultValue = 2, MinValue = 1, MaxValue = 10)]
        public int MaxUsdSensitivePositions { get; set; }

        [Parameter("Reference Initial Balance", Group = "Risk", DefaultValue = 0.0, MinValue = 0.0)]
        public double ReferenceInitialBalance { get; set; }

        [Parameter("Daily Loss Limit %", Group = "Risk", DefaultValue = 2.0, MinValue = 0.25, MaxValue = 10.0, Step = 0.25)]
        public double DailyLossLimitPercent { get; set; }

        [Parameter("Overall Loss Limit %", Group = "Risk", DefaultValue = 6.0, MinValue = 1.0, MaxValue = 20.0, Step = 0.5)]
        public double OverallLossLimitPercent { get; set; }

        [Parameter("Reset Stored Risk State", Group = "Risk", DefaultValue = false)]
        public bool ResetStoredRiskState { get; set; }

        [Parameter("Breakout Lookback", Group = "Strategy", DefaultValue = 55, MinValue = 10, MaxValue = 200)]
        public int BreakoutLookback { get; set; }

        [Parameter("ADX Period", Group = "Strategy", DefaultValue = 14, MinValue = 5, MaxValue = 50)]
        public int AdxPeriod { get; set; }

        [Parameter("Min ADX", Group = "Strategy", DefaultValue = 20.0, MinValue = 5.0, MaxValue = 60.0, Step = 0.5)]
        public double MinAdx { get; set; }

        [Parameter("ATR Period", Group = "Strategy", DefaultValue = 14, MinValue = 5, MaxValue = 50)]
        public int AtrPeriod { get; set; }

        [Parameter("Min Body x ATR", Group = "Strategy", DefaultValue = 0.4, MinValue = 0.1, MaxValue = 2.0, Step = 0.05)]
        public double MinBodyAtr { get; set; }

        [Parameter("SL x ATR", Group = "Strategy", DefaultValue = 1.5, MinValue = 0.5, MaxValue = 5.0, Step = 0.1)]
        public double StopAtrMultiple { get; set; }

        [Parameter("Reward Risk", Group = "Strategy", DefaultValue = 3.0, MinValue = 1.0, MaxValue = 6.0, Step = 0.25)]
        public double RewardRisk { get; set; }

        [Parameter("Max Entry Delay Minutes", Group = "Strategy", DefaultValue = 2, MinValue = 0, MaxValue = 15)]
        public int MaxEntryDelayMinutes { get; set; }

        [Parameter("Max Hold Hours", Group = "Strategy", DefaultValue = 24, MinValue = 1, MaxValue = 72)]
        public int MaxHoldHours { get; set; }

        [Parameter("Friday Entry Cutoff UTC Hour", Group = "Strategy", DefaultValue = 20, MinValue = 0, MaxValue = 23)]
        public int FridayEntryCutoffHourUtc { get; set; }

        [Parameter("Friday Force Close UTC Hour", Group = "Strategy", DefaultValue = 20, MinValue = 0, MaxValue = 23)]
        public int FridayForceCloseHourUtc { get; set; }

        [Parameter("Friday Force Close UTC Minute", Group = "Strategy", DefaultValue = 55, MinValue = 0, MaxValue = 59)]
        public int FridayForceCloseMinuteUtc { get; set; }

        [Parameter("News Filter Enabled", Group = "News", DefaultValue = true)]
        public bool NewsFilterEnabled { get; set; }

        [Parameter("Require News Schedule", Group = "News", DefaultValue = true)]
        public bool RequireNewsSchedule { get; set; }

        [Parameter("News Before Minutes", Group = "News", DefaultValue = 30, MinValue = 0, MaxValue = 180)]
        public int NewsBeforeMinutes { get; set; }

        [Parameter("News After Minutes", Group = "News", DefaultValue = 30, MinValue = 0, MaxValue = 180)]
        public int NewsAfterMinutes { get; set; }

        [Parameter("Flatten Before News", Group = "News", DefaultValue = true)]
        public bool FlattenBeforeNews { get; set; }

        [Parameter("Flatten Minutes Before", Group = "News", DefaultValue = 15, MinValue = 0, MaxValue = 120)]
        public int FlattenMinutesBeforeNews { get; set; }

        [Parameter("News Times UTC", Group = "News", DefaultValue = "")]
        public string NewsTimesUtc { get; set; }

        [Parameter("Verbose Logging", Group = "Diagnostics", DefaultValue = true)]
        public bool VerboseLogging { get; set; }

        private string StoragePrefix
        {
            get
            {
                var raw = string.IsNullOrWhiteSpace(BotLabel) ? DefaultLabel : BotLabel;
                var cleaned = new string(raw.Where(char.IsLetterOrDigit).ToArray());
                if (cleaned.Length > 20)
                    cleaned = cleaned.Substring(0, 20);
                var accountPrefix = "A" + Account.Number.ToString(CultureInfo.InvariantCulture);
                return accountPrefix + (string.IsNullOrWhiteSpace(cleaned) ? "H155Multi" : cleaned);
            }
        }

        protected override void OnStart()
        {
            if (string.IsNullOrWhiteSpace(BotLabel))
                BotLabel = DefaultLabel;

            if (ResetStoredRiskState)
                ResetRiskState();

            ParseNewsSchedule();
            RestoreRiskState();
            BuildInstrumentContexts();

            if (_contexts.Count == 0)
            {
                Print("STOP: none of the configured symbols exist on this cTrader account. Update Symbol Groups with exact broker symbol names.");
                Stop();
                return;
            }

            foreach (var context in _contexts.Values)
                context.H1Bars.BarOpened += OnAnyH1BarOpened;

            Positions.Closed += OnPositionClosed;
            Timer.Start(TimeSpan.FromSeconds(10));

            Print("START {0} | mode={1} | symbols={2} | risk/trade={3:F2}% | maxAggRisk={4:F2}% | daily={5:F2}% | overall={6:F2}% | newsEvents={7}",
                BotLabel, Mode, string.Join(",", _contexts.Keys), RiskPerTradePercent,
                MaxAggregatePlannedRiskPercent, DailyLossLimitPercent, OverallLossLimitPercent, _newsTimesUtc.Count);

            if (NewsFilterEnabled && RequireNewsSchedule && _newsTimesUtc.Count == 0)
                Print("SAFE LOCK: News filter requires a schedule, but News Times UTC is empty/invalid. New entries are blocked.");
        }

        protected override void OnTimer()
        {
            RefreshDailyStateIfNeeded();
            EnforceRiskGuards();
            ManageOpenPositions();
        }

        protected override void OnStop()
        {
            foreach (var context in _contexts.Values)
                context.H1Bars.BarOpened -= OnAnyH1BarOpened;
            Positions.Closed -= OnPositionClosed;
            SaveRiskState();
        }

        private void BuildInstrumentContexts()
        {
            _contexts.Clear();
            var groups = (SymbolGroups ?? string.Empty)
                .Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawGroup in groups)
            {
                var aliases = rawGroup.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToArray();

                if (aliases.Length == 0)
                    continue;

                Symbol resolved = null;
                foreach (var alias in aliases)
                {
                    var candidate = Symbols.GetSymbol(alias);
                    if (candidate == null)
                        continue;
                    resolved = candidate;
                    break;
                }

                if (resolved == null)
                {
                    Print("SYMBOL SKIP: none of aliases [{0}] exist on this account.", string.Join("|", aliases));
                    continue;
                }

                if (_contexts.ContainsKey(resolved.Name))
                    continue;

                var h1Bars = MarketData.GetBars(TimeFrame.Hour, resolved.Name);
                var h4Bars = MarketData.GetBars(TimeFrame.Hour4, resolved.Name);
                var context = new InstrumentContext
                {
                    SymbolName = resolved.Name,
                    Symbol = resolved,
                    H1Bars = h1Bars,
                    H4Bars = h4Bars,
                    H4Ema50 = Indicators.ExponentialMovingAverage(h4Bars.ClosePrices, 50),
                    H4Ema200 = Indicators.ExponentialMovingAverage(h4Bars.ClosePrices, 200),
                    H1Atr = Indicators.AverageTrueRange(h1Bars, AtrPeriod, MovingAverageType.Exponential),
                    H1Dms = Indicators.DirectionalMovementSystem(h1Bars, AdxPeriod),
                    IsCrypto = IsCryptoSymbol(resolved.Name),
                    IsUsdSensitive = IsUsdSensitiveSymbol(resolved.Name)
                };

                _contexts.Add(resolved.Name, context);
                Print("SYMBOL READY: group={0} -> {1} | crypto={2} | USD-sensitive={3}",
                    rawGroup.Trim(), resolved.Name, context.IsCrypto, context.IsUsdSensitive);
            }
        }

        private void OnAnyH1BarOpened(BarOpenedEventArgs args)
        {
            if (args == null || args.Bars == null)
                return;

            if (!_contexts.TryGetValue(args.Bars.SymbolName, out var context))
                return;

            RefreshDailyStateIfNeeded();
            EnforceRiskGuards();

            if (!CanConsiderNewEntry(context, out var blockReason))
            {
                Log($"{context.SymbolName} ENTRY BLOCKED: {blockReason}");
                return;
            }

            if (context.H1Bars.Count < BreakoutLookback + 5 || context.H4Bars.Count < 205)
            {
                Log($"{context.SymbolName} ENTRY BLOCKED: insufficient history.");
                return;
            }

            var signal = context.H1Bars.Last(1);
            var signalCloseTimeUtc = signal.OpenTime.AddHours(1);
            if (signalCloseTimeUtc == context.LastSignalTimeUtc)
                return;
            context.LastSignalTimeUtc = signalCloseTimeUtc;

            if (!context.IsCrypto || !AllowCryptoWeekend)
            {
                if (signalCloseTimeUtc.DayOfWeek == DayOfWeek.Friday && signalCloseTimeUtc.Hour >= FridayEntryCutoffHourUtc)
                {
                    Log($"{context.SymbolName} NO TRADE {signalCloseTimeUtc:o}: Friday confirmation cutoff.");
                    return;
                }
            }

            var h4 = context.H4Bars.Last(1);
            var ema50 = context.H4Ema50.Result.Last(1);
            var ema200 = context.H4Ema200.Result.Last(1);
            var h4Long = ema50 > ema200 && h4.Close > ema50;
            var h4Short = ema50 < ema200 && h4.Close < ema50;

            var adx = context.H1Dms.ADX.Last(1);
            var atr = context.H1Atr.Result.Last(1);
            var body = Math.Abs(signal.Close - signal.Open);
            var bodyAtr = atr > 0 ? body / atr : 0;

            double previousHigh = double.MinValue;
            double previousLow = double.MaxValue;
            for (var offset = 2; offset <= BreakoutLookback + 1; offset++)
            {
                previousHigh = Math.Max(previousHigh, context.H1Bars.Last(offset).High);
                previousLow = Math.Min(previousLow, context.H1Bars.Last(offset).Low);
            }

            var breaksUp = signal.Close > previousHigh;
            var breaksDown = signal.Close < previousLow;
            var qualityOk = adx >= MinAdx && bodyAtr >= MinBodyAtr;

            Log(string.Format(CultureInfo.InvariantCulture,
                "{0} SCAN {1:o} | H4 long={2} short={3} | close={4} | prevHigh={5} prevLow={6} | ADX={7:F2} | bodyATR={8:F2} | up={9} down={10}",
                context.SymbolName, signalCloseTimeUtc, h4Long, h4Short, signal.Close,
                previousHigh, previousLow, adx, bodyAtr, breaksUp, breaksDown));

            if (!qualityOk)
                return;

            if (h4Long && breaksUp)
                ProcessSignal(context, TradeType.Buy, signal, atr);
            else if (h4Short && breaksDown)
                ProcessSignal(context, TradeType.Sell, signal, atr);
        }

        private void ProcessSignal(InstrumentContext context, TradeType tradeType, Bar signalBar, double atr)
        {
            var now = Server.TimeInUtc;
            var confirmationTimeUtc = signalBar.OpenTime.AddHours(1);
            var entryDelay = now - confirmationTimeUtc;
            if (entryDelay.TotalMinutes < 0 || entryDelay.TotalMinutes > MaxEntryDelayMinutes)
            {
                Log($"{context.SymbolName} SIGNAL REJECTED: entry delay {entryDelay.TotalMinutes:F2}m outside 0..{MaxEntryDelayMinutes}m.");
                return;
            }

            if (IsNewsBlackout(now, out var newsTime))
            {
                Log($"{context.SymbolName} SIGNAL REJECTED: news blackout around {newsTime:o}");
                return;
            }

            var stopPrice = tradeType == TradeType.Buy
                ? signalBar.Close - StopAtrMultiple * atr
                : signalBar.Close + StopAtrMultiple * atr;

            var expectedEntry = tradeType == TradeType.Buy ? context.Symbol.Ask : context.Symbol.Bid;
            var stopLossPips = tradeType == TradeType.Buy
                ? (expectedEntry - stopPrice) / context.Symbol.PipSize
                : (stopPrice - expectedEntry) / context.Symbol.PipSize;

            if (stopLossPips <= 0 || double.IsNaN(stopLossPips) || double.IsInfinity(stopLossPips))
            {
                Log($"{context.SymbolName} SIGNAL REJECTED: invalid SL distance {stopLossPips}");
                return;
            }

            var takeProfitPips = stopLossPips * RewardRisk;
            var riskAmount = _initialReferenceEquity * RiskPerTradePercent / 100.0;
            var volume = context.Symbol.VolumeForFixedRisk(riskAmount, stopLossPips, RoundingMode.Down);
            volume = context.Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            if (volume < context.Symbol.VolumeInUnitsMin)
            {
                Log($"{context.SymbolName} SIGNAL REJECTED: calculated volume {volume} < minimum {context.Symbol.VolumeInUnitsMin}.");
                return;
            }

            if (volume > context.Symbol.VolumeInUnitsMax)
                volume = context.Symbol.VolumeInUnitsMax;

            Print("SIGNAL {0} {1} | time={2:o} | entry~{3} | SLpips={4:F2} | TPpips={5:F2} | risk={6:F2} | volume={7}",
                context.SymbolName, tradeType, confirmationTimeUtc, expectedEntry, stopLossPips, takeProfitPips, riskAmount, volume);

            if (Mode == ExecutionMode.SignalOnly)
            {
                Print("SIGNAL ONLY: no order sent for {0}.", context.SymbolName);
                return;
            }

            var result = ExecuteMarketOrder(tradeType, context.SymbolName, volume, BotLabel,
                stopLossPips, takeProfitPips,
                $"H1-55 ADX>={MinAdx} ATRSL={StopAtrMultiple} RR={RewardRisk}");

            if (!result.IsSuccessful)
            {
                Print("ORDER FAILED {0}: {1}", context.SymbolName, result.Error);
                return;
            }

            Print("ORDER OPENED: symbol={0} id={1} entry={2} SL={3} TP={4}",
                context.SymbolName, result.Position.Id, result.Position.EntryPrice,
                result.Position.StopLoss, result.Position.TakeProfit);
        }

        private bool CanConsiderNewEntry(InstrumentContext context, out string reason)
        {
            if (_overallLocked)
            {
                reason = "overall loss lock active";
                return false;
            }

            if (_dailyLocked)
            {
                reason = "daily loss lock active";
                return false;
            }

            var botPositions = GetBotPositions().ToArray();
            if (botPositions.Any(p => string.Equals(p.SymbolName, context.SymbolName, StringComparison.OrdinalIgnoreCase)))
            {
                reason = "bot position already open on this symbol";
                return false;
            }

            if (botPositions.Length >= MaxConcurrentPositions)
            {
                reason = $"max concurrent positions reached ({MaxConcurrentPositions})";
                return false;
            }

            var plannedRiskAfterEntry = (botPositions.Length + 1) * RiskPerTradePercent;
            if (plannedRiskAfterEntry > MaxAggregatePlannedRiskPercent + 1e-9)
            {
                reason = $"aggregate planned risk would be {plannedRiskAfterEntry:F2}% > {MaxAggregatePlannedRiskPercent:F2}%";
                return false;
            }

            if (context.IsUsdSensitive)
            {
                var usdSensitiveOpen = botPositions.Count(p =>
                    _contexts.TryGetValue(p.SymbolName, out var openContext) && openContext.IsUsdSensitive);
                if (usdSensitiveOpen >= MaxUsdSensitivePositions)
                {
                    reason = $"USD-sensitive position cap reached ({MaxUsdSensitivePositions})";
                    return false;
                }
            }

            if (NewsFilterEnabled && RequireNewsSchedule && _newsTimesUtc.Count == 0)
            {
                reason = "news schedule required but missing";
                return false;
            }

            if (IsNewsBlackout(Server.TimeInUtc, out var newsTime))
            {
                reason = $"news blackout around {newsTime:o}";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private void ManageOpenPositions()
        {
            var now = Server.TimeInUtc;
            var botPositions = GetBotPositions().ToArray();
            if (botPositions.Length == 0)
                return;

            if (FlattenBeforeNews && NewsFilterEnabled && TryGetUpcomingNews(now, out var nextNews))
            {
                var minutes = (nextNews - now).TotalMinutes;
                if (minutes >= 0 && minutes <= FlattenMinutesBeforeNews)
                {
                    CloseBotPositions($"pre-news flatten for {nextNews:o}");
                    return;
                }
            }

            foreach (var position in botPositions)
            {
                if ((now - position.EntryTime).TotalHours >= MaxHoldHours)
                {
                    Print("TIME EXIT: {0} position {1} exceeded {2} hours.", position.SymbolName, position.Id, MaxHoldHours);
                    ClosePosition(position);
                    continue;
                }

                var isCrypto = _contexts.TryGetValue(position.SymbolName, out var context) && context.IsCrypto;
                if ((!isCrypto || !AllowCryptoWeekend) && IsAtOrAfterFridayForceClose(now))
                {
                    Print("FRIDAY EXIT: closing {0} position {1} at/after cutoff.", position.SymbolName, position.Id);
                    ClosePosition(position);
                }
            }
        }

        private IEnumerable<Position> GetBotPositions()
        {
            return Positions.Where(p => string.Equals(p.Label, BotLabel, StringComparison.Ordinal));
        }

        private void EnforceRiskGuards()
        {
            if (_initialReferenceEquity <= 0)
                return;

            var overallFloor = _initialReferenceEquity * (1.0 - OverallLossLimitPercent / 100.0);
            if (!_overallLocked && Account.Equity <= overallFloor)
            {
                _overallLocked = true;
                Print("OVERALL GUARD TRIGGERED: equity={0:F2}, floor={1:F2}. Bot locked until intentional state reset.",
                    Account.Equity, overallFloor);
                CloseBotPositions("overall loss guard");
                SaveRiskState();
            }

            var dailyFloor = _dayStartEquity - (_initialReferenceEquity * DailyLossLimitPercent / 100.0);
            if (!_dailyLocked && Account.Equity <= dailyFloor)
            {
                _dailyLocked = true;
                Print("DAILY GUARD TRIGGERED: equity={0:F2}, floor={1:F2}. Locked until next UTC day.",
                    Account.Equity, dailyFloor);
                CloseBotPositions("daily loss guard");
                SaveRiskState();
            }
        }

        private void RefreshDailyStateIfNeeded()
        {
            var today = Server.TimeInUtc.Date;
            if (_dayUtc == today)
                return;

            _dayUtc = today;
            _dayStartEquity = Account.Equity;
            _dailyLocked = false;
            SaveRiskState();
            Print("NEW UTC DAY: day-start equity reset to {0:F2}", _dayStartEquity);
        }

        private void RestoreRiskState()
        {
            var storedInitial = ParseDouble(LocalStorage.GetString(InitialEquityKey, RiskStorageScope));
            _initialReferenceEquity = ReferenceInitialBalance > 0
                ? ReferenceInitialBalance
                : storedInitial > 0 ? storedInitial : Account.Equity;

            var storedDay = ParseDate(LocalStorage.GetString(DayDateKey, RiskStorageScope));
            var storedDayEquity = ParseDouble(LocalStorage.GetString(DayEquityKey, RiskStorageScope));
            var today = Server.TimeInUtc.Date;

            if (storedDay == today && storedDayEquity > 0)
            {
                _dayUtc = storedDay;
                _dayStartEquity = storedDayEquity;
                _dailyLocked = ParseBool(LocalStorage.GetString(DailyLockKey, RiskStorageScope));
            }
            else
            {
                _dayUtc = today;
                _dayStartEquity = Account.Equity;
                _dailyLocked = false;
            }

            _overallLocked = ParseBool(LocalStorage.GetString(OverallLockKey, RiskStorageScope));
            SaveRiskState();
        }

        private void SaveRiskState()
        {
            LocalStorage.SetString(InitialEquityKey, _initialReferenceEquity.ToString("R", CultureInfo.InvariantCulture), RiskStorageScope);
            LocalStorage.SetString(DayEquityKey, _dayStartEquity.ToString("R", CultureInfo.InvariantCulture), RiskStorageScope);
            LocalStorage.SetString(DayDateKey, _dayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), RiskStorageScope);
            LocalStorage.SetString(DailyLockKey, _dailyLocked ? "true" : "false", RiskStorageScope);
            LocalStorage.SetString(OverallLockKey, _overallLocked ? "true" : "false", RiskStorageScope);
            LocalStorage.Flush(RiskStorageScope);
        }

        private void ResetRiskState()
        {
            LocalStorage.Remove(InitialEquityKey, RiskStorageScope);
            LocalStorage.Remove(DayEquityKey, RiskStorageScope);
            LocalStorage.Remove(DayDateKey, RiskStorageScope);
            LocalStorage.Remove(DailyLockKey, RiskStorageScope);
            LocalStorage.Remove(OverallLockKey, RiskStorageScope);
            LocalStorage.Flush(RiskStorageScope);
            Print("Stored risk state reset requested. Set the parameter back to false after this start.");
        }

        private void ParseNewsSchedule()
        {
            _newsTimesUtc.Clear();
            if (string.IsNullOrWhiteSpace(NewsTimesUtc))
                return;

            var parts = NewsTimesUtc.Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var formats = new[]
            {
                "yyyy-MM-dd HH:mm",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-ddTHH:mm",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm:ssZ"
            };

            foreach (var raw in parts)
            {
                var text = raw.Trim();
                if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                {
                    _newsTimesUtc.Add(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
                }
                else
                {
                    Print("NEWS PARSE WARNING: could not parse '{0}' as UTC.", text);
                }
            }

            _newsTimesUtc.Sort();
        }

        private bool IsNewsBlackout(DateTime nowUtc, out DateTime matchedNews)
        {
            matchedNews = DateTime.MinValue;
            if (!NewsFilterEnabled || _newsTimesUtc.Count == 0)
                return false;

            foreach (var news in _newsTimesUtc)
            {
                var start = news.AddMinutes(-NewsBeforeMinutes);
                var end = news.AddMinutes(NewsAfterMinutes);
                if (nowUtc >= start && nowUtc <= end)
                {
                    matchedNews = news;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetUpcomingNews(DateTime nowUtc, out DateTime nextNews)
        {
            nextNews = _newsTimesUtc.FirstOrDefault(x => x >= nowUtc);
            return nextNews != default(DateTime);
        }

        private bool IsAtOrAfterFridayForceClose(DateTime nowUtc)
        {
            if (nowUtc.DayOfWeek != DayOfWeek.Friday)
                return false;

            var cutoff = nowUtc.Date
                .AddHours(FridayForceCloseHourUtc)
                .AddMinutes(FridayForceCloseMinuteUtc);
            return nowUtc >= cutoff;
        }

        private void CloseBotPositions(string reason)
        {
            foreach (var position in GetBotPositions().ToArray())
            {
                Print("CLOSE BOT POSITION {0} {1}: {2}", position.SymbolName, position.Id, reason);
                ClosePosition(position);
            }
        }

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            if (args.Position.Label != BotLabel)
                return;

            Print("CLOSED symbol={0} id={1} reason={2} net={3:F2} entry={4} closeTime={5:o}",
                args.Position.SymbolName, args.Position.Id, args.Reason, args.Position.NetProfit,
                args.Position.EntryPrice, Server.TimeInUtc);
        }

        private static bool IsCryptoSymbol(string symbolName)
        {
            var s = (symbolName ?? string.Empty).ToUpperInvariant();
            return s.Contains("BTC") || s.Contains("ETH") || s.Contains("XRP") ||
                   s.Contains("SOL") || s.Contains("LTC") || s.Contains("DOGE");
        }

        private static bool IsUsdSensitiveSymbol(string symbolName)
        {
            var s = (symbolName ?? string.Empty).ToUpperInvariant();
            return s.Contains("USD") || s.Contains("XAU") || s.Contains("GOLD") ||
                   s.Contains("BTC") || s.Contains("US100") || s.Contains("NAS") ||
                   s.Contains("US500") || s.Contains("SPX") || s.Contains("US30");
        }

        private void Log(string message)
        {
            if (VerboseLogging)
                Print(message);
        }

        private static double ParseDouble(string value)
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                return result;
            return 0;
        }

        private static DateTime ParseDate(string value)
        {
            if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result))
                return result.Date;
            return DateTime.MinValue;
        }

        private static bool ParseBool(string value)
        {
            return bool.TryParse(value, out var result) && result;
        }
    }
}
