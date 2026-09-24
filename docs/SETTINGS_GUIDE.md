# cTrader settings guide

All deployment-sensitive values are Parameters. No source edit is required.

## Account Profile
- Funded / Live Profile
- Use Profile News Preset
- Account Start Balance

## Markets
- Trade Symbols: native multi-symbol picker
- Extra Symbol Aliases: optional portability fallback
- Allow Crypto Weekend

## News
- News Source: Manual_UTC / HTTP_Auto_Feed / Disabled
- News Filter Enabled
- Require Valid News Schedule
- Position Policy
- No Entry Before (min)
- No Entry After (min)
- Flatten Before (min)
- Manual News Times UTC
- Auto News Feed URL
- Auto Refresh Minutes
- Fail-Lock on Feed Error
- Max Feed Age Minutes

## Risk
- Risk Per Trade %
- Max Aggregate Planned Risk %
- Max Concurrent Bot Positions
- Max USD-Sensitive Positions
- Daily Loss Limit %
- Overall Loss Limit %
- Reset Stored Risk State

## Safe starting setup
- Execution Mode: SignalOnly
- Risk/trade: 0.25%
- Max aggregate planned risk: 0.75%
- Daily guard: 2%
- Overall guard: 6%
- Require Valid News Schedule: true
- Fail-Lock on Feed Error: true

Profile presets are intentionally conservative. Firm rules can change, so verify the current agreement before enabling Trade mode.
