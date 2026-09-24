# H1 Breakout Bot — Multi-Asset cTrader cBot

Research/demo-first cTrader cBot implementing the H1 55-bar trend-breakout strategy as a multi-symbol engine.

## Default candidate universe

`XAUUSD|GOLD;BTCUSDT|BTCUSD;EURUSD;GBPUSD;USDJPY`

Each semicolon-separated item is one market. A pipe means aliases: the bot picks the first symbol that actually exists on the connected cTrader account. For example `BTCUSDT|BTCUSD` prevents trading both BTC variants by default.

Broker suffixes differ. If your broker exposes `XAUUSD.c`, `BTCUSD.`, etc., replace the parameter with the exact broker names. Missing symbols are skipped; the bot does not guess.

**Only XAUUSD currently has the longer research evidence used to choose this strategy. BTC/FX symbols are candidate markets and must be backtested/forward-tested before Trade mode is enabled.**

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
- Crypto: weekend handling is configurable; default `Allow Crypto Weekend = true`.

## Funded safety defaults

- `Execution Mode = SignalOnly` by default.
- Risk/trade: 0.25% of reference initial equity.
- Max aggregate planned bot risk: 0.75%.
- Max concurrent bot positions: 3.
- Max USD-sensitive positions: 2.
- Daily equity loss lock: 2% of reference initial capital from UTC day-start equity.
- Overall equity loss lock: 6% below reference initial equity.
- Risk-lock state persists across restarts using cTrader LocalStorage.
- Guards close only positions carrying this bot label.

## News safety

V1 uses explicit UTC event timestamps. With `Require News Schedule=true`, missing news data fail-locks new entries.

- No new entry from 30 minutes before through 30 minutes after a listed event.
- Existing bot positions are flattened 15 minutes before a listed event when enabled.
- Pre-news signals are not reused after the blackout.

For multi-asset operation, load all relevant high-impact events for the enabled currencies/markets. V1 treats the supplied schedule as a global safety blackout.

## Build

```bash
dotnet restore src/MultiAssetH1BreakoutBot.csproj
dotnet build src/MultiAssetH1BreakoutBot.csproj -c Release
```

Or create a C# cBot in cTrader Algo, replace its source with `src/MultiAssetH1BreakoutBot.cs`, and Build.

## First deployment

1. Use demo / prop-firm free trial first.
2. Keep `Execution Mode = SignalOnly`.
3. Set exact broker symbols in `Symbol Groups`.
4. Set `Reference Initial Balance` to the challenge/funded starting balance.
5. Keep 0.25% risk, 2% daily lock, 6% overall lock and <=0.75% aggregate planned risk initially.
6. Load the current high-impact news schedule in UTC.
7. Confirm each symbol logs H4 bias, H1 breakout, ADX, body/ATR and risk/news blocks correctly.
8. Backtest each added market separately and then the combined portfolio before enabling Trade mode.

## Validation status

XAUUSD is the research-backed anchor candidate. BTCUSDT/BTCUSD, EURUSD, GBPUSD and USDJPY are configurable candidate markets; they are not assumed to inherit XAUUSD performance. Cross-market validation is required before live/funded Trade mode.
