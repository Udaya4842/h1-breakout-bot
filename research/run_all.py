#!/usr/bin/env python3
import argparse, json, subprocess, sys
from pathlib import Path
import pandas as pd

SYMS=['BTCUSDT','EURUSD','GBPUSD','USDJPY']

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--data-root',default='data')
    ap.add_argument('--reports-root',default='reports')
    ap.add_argument('--btc-cost-bps',type=float,default=5.0)
    a=ap.parse_args()

    root=Path(a.data_root)
    reports=Path(a.reports_root)
    reports.mkdir(parents=True,exist_ok=True)
    rows=[]

    for s in SYMS:
        base=root/('btc' if s=='BTCUSDT' else 'forex')
        out=reports/s
        cmd=[
            sys.executable,
            str(Path(__file__).with_name('backtest_h1_breakout.py')),
            '--symbol',s,
            '--m5',str(base/f'{s}_M5.csv'),
            '--h1',str(base/f'{s}_H1.csv'),
            '--h4',str(base/f'{s}_H4.csv'),
            '--outdir',str(out),
            '--btc-cost-bps',str(a.btc_cost_bps)
        ]
        subprocess.run(cmd,check=True)
        rows.append(json.loads((out/'summary.json').read_text()))

    df=pd.DataFrame(rows)
    df.to_csv(reports/'comparison.csv',index=False)
    md=[
        '# H1 55-bar breakout — 2-year cross-market screen',
        '',
        df.to_markdown(index=False),
        '',
        '## Notes',
        '- Fixed 0.25% initial-capital risk/trade.',
        '- SL-first on same lower-timeframe bar ambiguity (conservative).',
        '- FX uses Dukascopy bid OHLC + mean spread.',
        '- BTC uses Binance spot OHLC with configurable synthetic execution cost; this is screening, not cTrader CFD parity.',
        '- XAUUSD prior research is not recomputed by this workflow.'
    ]
    (reports/'SUMMARY.md').write_text('\n'.join(md))
    print(df.to_string(index=False))

if __name__=='__main__':
    main()
