#!/usr/bin/env python3
import argparse, json, math
from pathlib import Path
import numpy as np
import pandas as pd

def load_csv(path):
    df = pd.read_csv(path)
    df['time'] = pd.to_datetime(df['time'], utc=True)
    for c in ['open','high','low','close','volume','spread']:
        if c in df.columns:
            df[c] = pd.to_numeric(df[c], errors='coerce')
    if 'spread' not in df.columns:
        df['spread']=0.0
    return df.sort_values('time').drop_duplicates('time').reset_index(drop=True)

def wilder_rma(s, n):
    return s.ewm(alpha=1/n, adjust=False, min_periods=n).mean()

def indicators(h1, h4, lookback=55, atr_n=14, adx_n=14):
    h1 = h1.copy(); h4=h4.copy()
    prev = h1['close'].shift(1)
    tr = pd.concat([(h1.high-h1.low).abs(), (h1.high-prev).abs(), (h1.low-prev).abs()], axis=1).max(axis=1)
    h1['atr'] = wilder_rma(tr, atr_n)
    up = h1.high.diff(); dn = -h1.low.diff()
    plus_dm = pd.Series(np.where((up>dn)&(up>0), up, 0.0), index=h1.index)
    minus_dm = pd.Series(np.where((dn>up)&(dn>0), dn, 0.0), index=h1.index)
    atr_sm = wilder_rma(tr, adx_n)
    plus_di = 100*wilder_rma(plus_dm, adx_n)/atr_sm
    minus_di = 100*wilder_rma(minus_dm, adx_n)/atr_sm
    dx = 100*(plus_di-minus_di).abs()/(plus_di+minus_di).replace(0,np.nan)
    h1['adx'] = wilder_rma(dx, adx_n)
    h1['prev_high'] = h1.high.shift(1).rolling(lookback).max()
    h1['prev_low'] = h1.low.shift(1).rolling(lookback).min()
    h1['body_atr'] = (h1.close-h1.open).abs()/h1.atr

    h4['ema50'] = h4.close.ewm(span=50, adjust=False, min_periods=50).mean()
    h4['ema200'] = h4.close.ewm(span=200, adjust=False, min_periods=200).mean()
    h4['h4_long'] = (h4.ema50>h4.ema200)&(h4.close>h4.ema50)
    h4['h4_short'] = (h4.ema50<h4.ema200)&(h4.close<h4.ema50)

    hh=h1.copy(); hh['signal_close_time']=hh.time+pd.Timedelta(hours=1)
    h4x=h4[['time','h4_long','h4_short']].copy(); h4x['h4_close_time']=h4x.time+pd.Timedelta(hours=4)
    h4x=h4x.drop(columns='time').sort_values('h4_close_time')
    hh=pd.merge_asof(hh.sort_values('signal_close_time'), h4x, left_on='signal_close_time', right_on='h4_close_time', direction='backward')
    return hh

def run(symbol, m5, h1, h4, risk_pct=0.25, daily_limit=2.0, overall_limit=6.0, rr=3.0,
        stop_atr=1.5, min_adx=20.0, min_body_atr=0.4, lookback=55, max_hold_hours=24,
        entry_delay_minutes=2, crypto_cost_bps_each_side=5.0):
    h = indicators(h1,h4,lookback)
    trades=[]; initial=100.0; equity=100.0; peak=100.0; maxdd=0.0
    overall_locked=False; day=None; day_start=100.0; daily_locked=False
    active_until=pd.Timestamp.min.tz_localize('UTC')

    for _,r in h.iterrows():
        t=r['signal_close_time']
        if pd.isna(t) or pd.isna(r.atr) or pd.isna(r.adx) or pd.isna(r.h4_long):
            continue
        if day is None or t.date()!=day:
            day=t.date(); day_start=equity; daily_locked=False
        if overall_locked or daily_locked or t < active_until:
            continue
        if not ('BTC' in symbol.upper()) and t.dayofweek==4 and t.hour>=20:
            continue
        if r.adx < min_adx or r.body_atr < min_body_atr:
            continue

        long_sig = bool(r.h4_long) and r.close > r.prev_high
        short_sig = bool(r.h4_short) and r.close < r.prev_low
        if not (long_sig or short_sig):
            continue

        side='BUY' if long_sig else 'SELL'
        window=m5[(m5.time>=t)&(m5.time<=t+pd.Timedelta(minutes=entry_delay_minutes))]
        if window.empty:
            continue
        eb=window.iloc[0]; entry_time=eb.time; spread=float(eb.spread or 0)
        if 'BTC' in symbol.upper():
            spread = float(eb.open)*crypto_cost_bps_each_side/10000.0

        if side=='BUY':
            entry=float(eb.open)+spread
            stop=float(r.close)-stop_atr*float(r.atr)
            if stop>=entry:
                continue
            one_r=entry-stop; target=entry+rr*one_r
        else:
            entry=float(eb.open)
            stop=float(r.close)+stop_atr*float(r.atr)+spread
            if stop<=entry:
                continue
            one_r=stop-entry; target=entry-rr*one_r

        exit_deadline=entry_time+pd.Timedelta(hours=max_hold_hours)
        path=m5[(m5.time>=entry_time)&(m5.time<=exit_deadline)]
        outcome='TIME'
        exit_time=path.iloc[-1].time if len(path) else entry_time
        exit_price=float(path.iloc[-1].close) if len(path) else entry
        r_mult=0.0

        for _,b in path.iterrows():
            spr=float(b.spread or 0)
            if 'BTC' in symbol.upper():
                spr=float(b.open)*crypto_cost_bps_each_side/10000.0
            if side=='BUY':
                hit_sl=float(b.low)<=stop
                hit_tp=float(b.high)>=target
                if hit_sl and hit_tp:
                    outcome='SL'; exit_price=stop; r_mult=-1.0
                elif hit_sl:
                    outcome='SL'; exit_price=stop; r_mult=-1.0
                elif hit_tp:
                    outcome='TP'; exit_price=target; r_mult=rr
                else:
                    continue
            else:
                ask_high=float(b.high)+spr
                ask_low=float(b.low)+spr
                hit_sl=ask_high>=stop
                hit_tp=ask_low<=target
                if hit_sl and hit_tp:
                    outcome='SL'; exit_price=stop; r_mult=-1.0
                elif hit_sl:
                    outcome='SL'; exit_price=stop; r_mult=-1.0
                elif hit_tp:
                    outcome='TP'; exit_price=target; r_mult=rr
                else:
                    continue
            exit_time=b.time
            break

        if outcome=='TIME':
            if side=='BUY':
                r_mult=(float(exit_price)-entry)/one_r
            else:
                close_ask=float(exit_price)+(float(path.iloc[-1].spread or 0) if len(path) else spread)
                r_mult=(entry-close_ask)/one_r
            r_mult=max(-1.0,min(rr,r_mult))

        pnl_pct = risk_pct*r_mult
        equity += pnl_pct
        peak=max(peak,equity)
        maxdd=max(maxdd, peak-equity)
        trades.append(dict(symbol=symbol,side=side,signal_time=t.isoformat(),entry_time=entry_time.isoformat(),
                           exit_time=exit_time.isoformat(),entry=entry,stop=stop,target=target,outcome=outcome,
                           R=r_mult,pnl_pct=pnl_pct,equity=equity))
        active_until=exit_time

        if equity <= initial*(1-overall_limit/100):
            overall_locked=True
        if equity <= day_start-initial*daily_limit/100:
            daily_locked=True

    tr=pd.DataFrame(trades)
    if tr.empty:
        summary=dict(symbol=symbol,trades=0,wins=0,losses=0,win_rate=0,pf=0,net_R=0,return_pct=0,
                     max_dd_pct=0,max_losing_streak=0,profitable_months=0,losing_months=0,zero_months=0,
                     best_month_pct=0,worst_month_pct=0,overall_halt=overall_locked)
        return tr,pd.DataFrame(),summary

    tr['exit_time']=pd.to_datetime(tr.exit_time,utc=True)
    tr['month']=tr.exit_time.dt.to_period('M').astype(str)
    months=pd.period_range(pd.Timestamp(tr.exit_time.min()).to_period('M'),
                           pd.Timestamp(tr.exit_time.max()).to_period('M'), freq='M').astype(str)
    monthly=tr.groupby('month').pnl_pct.sum().reindex(months,fill_value=0).rename('return_pct').reset_index().rename(columns={'index':'month'})
    gains=tr.loc[tr.R>0,'R'].sum()
    losses=-tr.loc[tr.R<0,'R'].sum()
    pf=float(gains/losses) if losses>0 else math.inf

    streak=mx=0
    for x in tr.R:
        if x<0:
            streak+=1
            mx=max(mx,streak)
        else:
            streak=0

    summary=dict(symbol=symbol,trades=int(len(tr)),wins=int((tr.R>0).sum()),losses=int((tr.R<0).sum()),
                 win_rate=float((tr.R>0).mean()*100),pf=pf,net_R=float(tr.R.sum()),
                 return_pct=float(tr.pnl_pct.sum()),max_dd_pct=float(maxdd),max_losing_streak=int(mx),
                 profitable_months=int((monthly.return_pct>0).sum()),losing_months=int((monthly.return_pct<0).sum()),
                 zero_months=int((monthly.return_pct==0).sum()),best_month_pct=float(monthly.return_pct.max()),
                 worst_month_pct=float(monthly.return_pct.min()),overall_halt=bool(overall_locked))
    return tr,monthly,summary

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--symbol',required=True)
    ap.add_argument('--m5',required=True)
    ap.add_argument('--h1',required=True)
    ap.add_argument('--h4',required=True)
    ap.add_argument('--outdir',required=True)
    ap.add_argument('--btc-cost-bps',type=float,default=5.0)
    a=ap.parse_args()
    out=Path(a.outdir); out.mkdir(parents=True,exist_ok=True)
    tr,mo,s=run(a.symbol,load_csv(a.m5),load_csv(a.h1),load_csv(a.h4),crypto_cost_bps_each_side=a.btc_cost_bps)
    tr.to_csv(out/'trades.csv',index=False)
    mo.to_csv(out/'monthly.csv',index=False)
    (out/'summary.json').write_text(json.dumps(s,indent=2))
    print(json.dumps(s,indent=2))

if __name__=='__main__':
    main()
