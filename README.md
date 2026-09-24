# H1 Breakout Bot — Multi-Asset cTrader cBot

Research/demo-first cTrader cBot implementing the H1 55-bar trend-breakout strategy as a multi-symbol engine.

## No-code runtime settings

The four deployment items are now cTrader Parameters. You do **not** edit C# whenever the account, broker, firm or news setup changes.

### 1. Account Profile

Parameters:

- `Funded / Live Profile`: `Custom`, `FTMO_Evaluation`, `FTMO_Standard_Funded`, `FTMO_Swing`, `FundedNext_Challenge`, `FundedNext_Funded`, `Live_Broker`, `Other_Firm`.
- `Use Profile News Preset`: when true, the bot applies a conservative news-handling preset for the selected profile. When false, the News section is fully manual.
- `Account Start Balance`: the initial/reference balance used for fixed-risk sizing and the 6% overall guard. Set 0 only when you intentionally want the bot to capture the current equity as its reference.

Firm rules can change. The profile is a runtime setting, not a substitute for checking the current agreement before enabling Trade mode.

### 2. Broker symbols

- `Trade Symbols` is a native cTrader **multi-symbol picker**. Select the exact instruments exposed by the connected broker/account, for example `XAUUSD`, `BTCUSD`, `EURUSD`, `GBPUSD`, `USDJPY`.
- `Extra Symbol Aliases` is an optional portability fallback such as `BTCUSDT|BTCUSD;XAUUSD|GOLD`. For each pipe-separated alias group, the first available broker symbol is selected.

No code change is needed when moving from `XAUUSD` to `GOLD`, `BTCUSDT` to `BTCUSD`, or when enabling/disabling pairs.

### 3. News source and policy

`News Source`:

- `Manual_UTC` — paste event timestamps into `Manual News Times UTC`.
- `HTTP_Auto_Feed` — set `Auto News Feed URL`; the bot refreshes it automatically.
- `Disabled` — no news filtering.

Other News parameters:

- `Position Policy`: block new entries only, or flatten bot positions before news.
- `No Entry Before (min)`
- `No Entry After (min)`
- `Flatten Before (min)`
- `Require Valid News Schedule`
- `Auto Refresh Minutes`
- `Fail-Lock on Feed Error`
- `Max Feed Age Minutes`

The HTTP endpoint may return plain text, CSV-like text or JSON containing UTC timestamps in formats such as `2026-10-02 12:30` or `2026-10-02T12:30:00Z`. The bot extracts those timestamps. If a required auto feed is missing, stale or invalid and fail-lock is enabled, new entries are blocked.

### 4. Execution safety

- `Execution Mode = SignalOnly` remains the default. It sends no orders.
- Change to `Trade` only after build/demo validation.
- Risk/trade default: 0.25%.
- Max aggregate planned bot risk default: 0.75%.
- Max concurrent bot positions default: 3.
- Max USD-sensitive positions default: 2.
- Daily equity loss lock default: 2%.
- Overall equity loss lock default: 6%.
- Risk-lock state persists across restarts.

## Strategy rules

For every configured symbol independently:

- H4 trend bias: EMA50/EMA200 + price relative to EMA50.
- H1 trigger: close beyond the previous 55 completed H1 highs/lows.
- Quality: ADX14 >= 20 and signal candle body >= 0.4 x H1 ATR14.
- Stop: 1.5 x H1 ATR14 from signal close, converted to actual entry-to-stop pips.
- Target: fixed 3R.
- Entry only after H1 close; max delay defaults to 2 minutes.
- Max hold: 24 hours.
- Non-crypto: Friday new-entry cutoff 20:00 UTC and force-close 20:55 UTC.
- Crypto weekend handling is configurable.

## Current market validation status

XAUUSD is the research-backed anchor candidate. BTC and FX symbols are candidate markets and must be backtested/forward-tested independently; they are not assumed to inherit XAUUSD performance.

## Build

```bash
dotnet restore src/MultiAssetH1BreakoutBot.csproj
dotnet build src/MultiAssetH1BreakoutBot.csproj -c Release
```

Or create a C# cBot in cTrader Algo, replace its source with `src/MultiAssetH1BreakoutBot.cs`, and Build.

## First deployment

1. Use demo / prop-firm free trial first.
2. Keep `Execution Mode = SignalOnly`.
3. Choose the firm/live profile.
4. Enter the exact account start balance.
5. Pick the exact broker symbols from `Trade Symbols`.
6. Configure `Manual_UTC` or `HTTP_Auto_Feed` news mode.
7. Verify logs and risk/news locks.
8. Backtest each added market separately and then the combined portfolio.
9. Enable `Trade` only after validation.
