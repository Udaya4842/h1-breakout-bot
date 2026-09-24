# Strategy specification

## Signal

Per configured symbol:

1. Use only completed H4/H1 candles.
2. H4 long regime: EMA50 > EMA200 and H4 close > EMA50.
3. H4 short regime: EMA50 < EMA200 and H4 close < EMA50.
4. H1 long trigger: close > highest high of preceding 55 completed H1 bars.
5. H1 short trigger: close < lowest low of preceding 55 completed H1 bars.
6. ADX14 >= 20.
7. Absolute H1 candle body / ATR14 >= 0.4.
8. Entry occurs only after H1 close, within 2 minutes by default.
9. Stop = 1.5 x ATR14 from signal close; actual entry-to-stop distance determines risk and 1R.
10. Target = 3R. No trailing, BE move or partial close in v1.
11. One position per symbol.
12. Max hold 24 hours.

## Portfolio controls

- Default fixed risk per accepted trade: 0.25% initial reference equity.
- Default aggregate planned bot risk ceiling: 0.75%.
- Default max open bot positions: 3.
- Default max USD-sensitive positions: 2.
- 2% daily equity guard and 6% overall equity guard.
- News fail-lock when schedule is required but absent.

## Market-status notes

The same rule engine is parameter-identical across symbols in v1 so cross-market tests are honest. Any per-symbol tuning becomes a separately registered strategy variant.
