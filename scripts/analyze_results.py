"""Aggregate preserved Unity JSONL, without changing raw observations."""
import csv
import json
import math
import re
import statistics
from pathlib import Path
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import numpy as np

ROOT=Path(__file__).resolve().parents[1]
RESULTS=ROOT/'results'

def match(text,variants):
    text=(text or '').casefold().replace('ё','е')
    for v in variants:
        v=v.casefold().replace('ё','е')
        if v.isdigit():
            if re.search(r'(?<!\d)'+re.escape(v)+r'(?!\d)',text):return True
        elif v in text:return True
    return False

def wilson(k,n):
    z=1.96;p=k/n;den=1+z*z/n;centre=(p+z*z/(2*n))/den;half=z*math.sqrt(p*(1-p)/n+z*z/(4*n*n))/den
    return [centre-half,centre+half]

def main():
    rows=[]
    for name in ['local','cloud']:
        for line in (RESULTS/f'evaluation-{name}.jsonl').read_text().splitlines():
            r=json.loads(line);t=r.get('trace') or {};completion=t.get('Completion') or {};raw={}
            try:raw=json.loads(t.get('Raw') or '{}')
            except (ValueError,TypeError):pass
            retrieved=t.get('Retrieved') or []
            ranks=[i+1 for i,c in enumerate(retrieved[:3]) if c['DocumentId']==r['source']]
            rows.append(dict(id=r['id'],configuration=r['configuration'],question=r['question'],model=completion.get('Model',''),
                fact_match=match(raw.get('speech',''),r['expected']),delivered_fact_match=match(t.get('Speech',''),r['expected']),
                json_valid=r.get('json_valid',False),accepted=(t.get('Validation') or {}).get('Accepted',False),
                hit_at_3=bool(ranks),reciprocal_rank=1/ranks[0] if ranks else 0,
                total_ms=r['wall_ms'],provider_ms=completion.get('ElapsedMs',0),retrieval_ms=t.get('RetrievalMs',0),
                input_tokens=completion.get('InputTokens',0),output_tokens=completion.get('OutputTokens',0),error=r.get('error') or '',speech=raw.get('speech','')))
    with (RESULTS/'metrics-per-question.csv').open('w',newline='') as f:
        w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
    summary={}
    for config in ['local-no-rag','local-rag','cloud-rag']:
        group=[r for r in rows if r['configuration']==config];n=len(group)
        summary[config]={'n':n,'model':next((r['model'] for r in group if r['model']),''),'fact_match_count':sum(r['fact_match'] for r in group),
            'delivered_fact_match_count':sum(r['delivered_fact_match'] for r in group),'json_valid_count':sum(r['json_valid'] for r in group),
            'accepted_count':sum(r['accepted'] for r in group),'hit_at_3_count':sum(r['hit_at_3'] for r in group),
            'mrr_at_3':statistics.mean(r['reciprocal_rank'] for r in group),'p50_ms':float(np.percentile([r['total_ms'] for r in group],50)),
            'p95_ms':float(np.percentile([r['total_ms'] for r in group],95)),
            'provider_p50_ms':float(np.percentile([r['provider_ms'] for r in group if r['provider_ms']],50)),
            'input_tokens':sum(r['input_tokens'] for r in group),'output_tokens':sum(r['output_tokens'] for r in group),
            'error_count':sum(bool(r['error']) for r in group),'api_payment_usd':0}
        summary[config]['fact_match_wilson_95']=wilson(summary[config]['fact_match_count'],n)
    terrain=json.loads((RESULTS/'terrain.json').read_text());ts=[]
    for resolution in [65,129,257,513]:
        a=[r['elapsed_ms'] for r in terrain if r['resolution']==resolution];ts.append({'resolution':resolution,'n':len(a),'median_ms':statistics.median(a),'p95_ms':float(np.percentile(a,95))})
    data={'qa':summary,'terrain':ts,'terrain_reproducible_count':sum(r['identical'] for r in terrain),'terrain_n':len(terrain),
          'method':'30 fixed questions; exact normalized answer aliases with digit boundaries; separate generated and delivered speech; linear percentile interpolation; memory accumulation disabled',
          'date':'2026-10-30','hardware':'Apple M4, 16 GiB unified memory, macOS 15.7.4','unity':'6000.3.27f1'}
    (RESULTS/'summary.json').write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n')
    figures=RESULTS/'figures';figures.mkdir(exist_ok=True)
    plt.rcParams.update({'font.family':'DejaVu Sans','font.size':11,'axes.spines.top':False,'axes.spines.right':False,'figure.dpi':160})
    labels=['Local без RAG','Local с RAG','Cloud с RAG'];colors=['#7b8792','#287e83','#274e75'];values=list(summary.values());x=np.arange(3)
    fig,ax=plt.subplots(figsize=(9,4.5));ax.bar(x-.17,[100*v['fact_match_count']/v['n'] for v in values],.34,label='Факт в ответе модели',color=colors);ax.bar(x+.17,[100*v['delivered_fact_match_count']/v['n'] for v in values],.34,label='Факт в принятой реплике',color=colors,alpha=.45)
    ax.set_xticks(x,labels);ax.set_ylabel('Доля вопросов, %');ax.set_ylim(0,105);ax.legend(frameon=False);ax.grid(axis='y',alpha=.15);fig.tight_layout();fig.savefig(figures/'quality.png');fig.savefig(figures/'quality.svg');plt.close(fig)
    fig,ax=plt.subplots(figsize=(9,4.5));ax.bar(x-.17,[v['p50_ms']/1000 for v in values],.34,label='p50 полной задержки',color=colors);ax.bar(x+.17,[v['provider_p50_ms']/1000 for v in values],.34,label='p50 HTTP обращения',color=colors,alpha=.45)
    ax.set_xticks(x,labels);ax.set_ylabel('Время, с');ax.legend(frameon=False);ax.grid(axis='y',alpha=.15);fig.tight_layout();fig.savefig(figures/'latency.png');fig.savefig(figures/'latency.svg');plt.close(fig)
    fig,ax=plt.subplots(figsize=(8,4));ax.plot([r['resolution'] for r in ts],[r['median_ms'] for r in ts],marker='o',color='#287e83',label='Медиана, 10 seed');ax.set_xlabel('Сторона heightmap, отсчётов');ax.set_ylabel('Generate, мс');ax.grid(alpha=.15);ax.legend(frameon=False);fig.tight_layout();fig.savefig(figures/'terrain.png');fig.savefig(figures/'terrain.svg');plt.close(fig)
    print(json.dumps(data,ensure_ascii=False,indent=2))

if __name__=='__main__':main()
