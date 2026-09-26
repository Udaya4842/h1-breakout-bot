# Cross-market backtest research

This folder contains the reproducible two-year screening pipeline for BTCUSDT, EURUSD, GBPUSD and USDJPY.

Data sources:
- FX: Dukascopy public feed via duka-data, compiled to M5/H1/H4 with spread.
- BTCUSDT: Binance public monthly 1-minute kline archive, resampled locally.

The workflow runs the frozen H1 55-bar breakout parameters at fixed 0.25% initial-capital risk/trade and saves per-market trades, monthly returns and summary metrics as a GitHub Actions artifact.

Public-run trigger: 2026-09-26
