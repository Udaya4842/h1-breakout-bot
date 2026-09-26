#!/usr/bin/env python3
import argparse
from pathlib import Path
import pandas as pd

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--symbol', required=True)
    ap.add_argument('--chunks-dir', default='data/fx_chunks')
    ap.add_argument('--outdir', default='data/forex')
    a=ap.parse_args()

    chunks=sorted(Path(a.chunks_dir).glob(f'{a.symbol}_*_M5.csv'))
    if not chunks:
        raise SystemExit(f'No M5 chunks found for {a.symbol}')

    frames=[]
    for p in chunks:
        df=pd.read_csv(p)
        df['time']=pd.to_datetime(df['time'], utc=True)
        frames.append(df)
        print(f'loaded {p}: {len(df)} rows')

    m5=(pd.concat(frames, ignore_index=True)
          .drop_duplicates('time')
          .sort_values('time')
          .reset_index(drop=True))

    for c in ['open','high','low','close','volume','spread']:
        if c in m5.columns:
            m5[c]=pd.to_numeric(m5[c], errors='coerce')
    m5=m5.dropna(subset=['time','open','high','low','close'])

    out=Path(a.outdir); out.mkdir(parents=True, exist_ok=True)
    m5.to_csv(out/f'{a.symbol}_M5.csv', index=False)

    x=m5.set_index('time')
    agg_map={'open':'first','high':'max','low':'min','close':'last'}
    if 'volume' in x.columns: agg_map['volume']='sum'
    if 'spread' in x.columns: agg_map['spread']='mean'

    for rule,label in [('1h','H1'),('4h','H4')]:
        y=x.resample(rule, label='left', closed='left').agg(agg_map).dropna(subset=['open','high','low','close']).reset_index()
        y.to_csv(out/f'{a.symbol}_{label}.csv', index=False)
        print(f'wrote {a.symbol}_{label}.csv: {len(y)} rows')

    print(f'wrote {a.symbol}_M5.csv: {len(m5)} rows from {len(chunks)} chunks')

if __name__=='__main__':
    main()
