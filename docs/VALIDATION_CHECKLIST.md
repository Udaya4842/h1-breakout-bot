# Validation checklist

- [ ] Build succeeds in current cTrader Algo.
- [ ] Exact broker symbols resolved and logged.
- [ ] XAUUSD signal-only replay matches expected H1/H4 logic.
- [ ] BTC symbol is exactly the broker-supported instrument (BTCUSDT or BTCUSD), not both by accident.
- [ ] EURUSD / GBPUSD / USDJPY each backtested separately with costs.
- [ ] Combined portfolio test enforces 0.75% planned-risk ceiling and USD-sensitive cap.
- [ ] Daily 2% and overall 6% equity locks tested with synthetic loss scenarios.
- [ ] Restart persistence tested for daily and overall locks.
- [ ] News schedule missing => new entries blocked.
- [ ] News blackout => no new entry.
- [ ] Pre-news flatten closes only this bot label.
- [ ] Non-crypto Friday cutoff/force-close tested.
- [ ] Crypto weekend behavior tested against selected broker/prop-firm rules.
- [ ] Demo/free-trial forward run completed before Trade mode.
