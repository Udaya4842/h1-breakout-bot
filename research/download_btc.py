#!/usr/bin/env python3
import argparse, io, zipfile, requests
from pathlib import Path
import pandas as pd

BASE = "https://data.binance.vision/data/spot/monthly/klines/{symbol}/1m/{symbol}-1m-{ym}.zip"
COLS = ["open_time","open","high","low","close","volume","close_time","quote_volume","trades","taker_buy_base","taker_buy_quote","ignore"]

def month_range(start, end):
    s = pd.Timestamp(start, tz='UTC').to_period('M')
    e = pd.Timestamp(end, tz='UTC').to_period('M')
    for p in pd.period_range(s, e, freq='M'):
        yield str(p)

def normalize_ts(series):
    x = pd.to_numeric(series, errors='coerce')
    unit = 'us' if x.dropna().median() > 10**14 else 'ms'
    return pd.to_datetime(x, unit=unit, utc=True, errors='coerce')

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--symbol', default='BTCUSDT')
    ap.add_argument('--start', required=True)
    ap.add_argument('--end', required=True)
    ap.add_argument('--outdir', default='data/btc')
    args = ap.parse_args()

    outdir = Path(args.outdir); outdir.mkdir(parents=True, exist_ok=True)
    frames=[]
    for ym in month_range(args.start, args.end):
        url = BASE.format(symbol=args.symbol.upper(), ym=ym)
        r = requests.get(url, timeout=60)
        if r.status_code == 404:
            print(f"missing monthly archive: {url}")
            continue
        r.raise_for_status()
        with zipfile.ZipFile(io.BytesIO(r.content)) as z:
            with z.open(z.namelist()[0]) as f:
                df = pd.read_csv(f, header=None, names=COLS)
        df['time'] = normalize_ts(df['open_time'])
        for c in ['open','high','low','close','volume']:
            df[c]=pd.to_numeric(df[c], errors='coerce')
        frames.append(df[['time','open','high','low','close','volume']].dropna())
        print(f"downloaded {args.symbol} {ym}: {len(df)} rows")

    if not frames:
        raise SystemExit('No BTC data downloaded')
    m1 = pd.concat(frames, ignore_index=True).drop_duplicates('time').sort_values('time')
    start = pd.Timestamp(args.start, tz='UTC'); end = pd.Timestamp(args.end, tz='UTC') + pd.Timedelta(days=1)
    m1 = m1[(m1.time >= start) & (m1.time < end)].copy()
    m1['spread'] = 0.0
    m1.to_csv(outdir/f'{args.symbol}_M1.csv', index=False)
    m1i = m1.set_index('time')
    def agg(rule):
        out = m1i.resample(rule, label='left', closed='left').agg({'open':'first','high':'max','low':'min','close':'last','volume':'sum','spread':'mean'}).dropna()
        return out.reset_index()
    agg('5min').to_csv(outdir/f'{args.symbol}_M5.csv', index=False)
    agg('1h').to_csv(outdir/f'{args.symbol}_H1.csv', index=False)
    agg('4h').to_csv(outdir/f'{args.symbol}_H4.csv', index=False)
    print(f"wrote {len(m1)} M1 rows and resampled M5/H1/H4")

if __name__ == '__main__':
    main()
