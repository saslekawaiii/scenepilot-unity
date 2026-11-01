"""Build and verify a two-level static TOC from actual PDF destinations.

Usage: python map_document_pages.py rendered.pdf [--verify] [--check-limits]
The PDF must come from the current DOCX manifest. A text occurrence in the TOC
is never accepted as the location of the corresponding section.
"""
from pathlib import Path
import argparse
import hashlib
import json
import re
from pypdf import PdfReader

ROOT=Path(__file__).resolve().parents[1]
BUILD=ROOT/'.private/build'


def normalize(text):
    return re.sub(r'\s+','',text or '').replace('\u00ad','')


def destinations(reader):
    def walk(nodes):
        for node in nodes:
            if isinstance(node,list):yield from walk(node)
            else:yield node
    result={}
    for destination in walk(reader.outline):
        key=normalize(destination.title)
        page=reader.get_destination_page_number(destination)+1
        result.setdefault(key,set()).add(page)
    return result


def map_pages(pdf,manifest):
    reader=PdfReader(pdf);outline=destinations(reader);mapping={}
    for title,_level in manifest['headings']:
        candidates=outline.get(normalize(title),set())
        if len(candidates)!=1:
            raise ValueError(f'Expected exactly one actual PDF destination for {title!r}; got {sorted(candidates)}')
        page=next(iter(candidates))
        page_text=normalize(reader.pages[page-1].extract_text())
        if normalize(title) not in page_text:
            raise ValueError(f'PDF destination/text mismatch on page {page}: {title}')
        mapping[title]=page
    return reader,mapping,outline


def verify_toc(reader,mapping,outline):
    toc=outline.get(normalize('СОДЕРЖАНИЕ'),set())
    if len(toc)!=1:raise ValueError('Cannot identify the actual TOC destination')
    first=next(iter(toc))
    abbreviation=outline.get(normalize('ПЕРЕЧЕНЬ СОКРАЩЕНИЙ'),set())
    after=min(abbreviation) if abbreviation else min(mapping.values())
    if after<=first:raise ValueError('Invalid TOC page interval')
    text=''.join(page.extract_text() for page in reader.pages[first-1:after-1])
    strip=lambda value:normalize(re.sub(r'[.·…]','',value))
    text=strip(text)
    mismatches=[{'title':title,'actual_page':page} for title,page in mapping.items() if strip(title)+str(page) not in text]
    return {'toc_pages':list(range(first,after)),'entries':len(mapping),'mismatches':mismatches}


def volume_limits(mapping,total):
    intro=mapping.get('ВВЕДЕНИЕ');conclusion=mapping.get('ЗАКЛЮЧЕНИЕ')
    chapter1=next((page for title,page in mapping.items() if title.startswith('1 ')),None)
    references=mapping.get('СПИСОК ИСПОЛЬЗОВАННЫХ ИСТОЧНИКОВ')
    appendix=next((page for title,page in mapping.items() if title.startswith('ПРИЛОЖЕНИЕ ')),None)
    if not all([intro,conclusion,chapter1,references,appendix]):raise ValueError('Missing required structural elements')
    result={'total_pages':total,'pages_before_appendices':appendix-1,'introduction_pages':chapter1-intro,'conclusion_pages':references-conclusion}
    result['checks']={'main_50_to_80':50<=result['pages_before_appendices']<=80,'intro_2_to_3':2<=result['introduction_pages']<=3,'conclusion_1_to_2':1<=result['conclusion_pages']<=2}
    return result


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('pdf',type=Path)
    parser.add_argument('--manifest',type=Path,default=BUILD/'document-manifest.json')
    parser.add_argument('--output',type=Path,default=BUILD/'page-map.json')
    parser.add_argument('--verify',action='store_true')
    parser.add_argument('--check-limits',action='store_true')
    parser.add_argument('--publish',action='store_true',help='Copy the verified map/statistics into public thesis sources')
    args=parser.parse_args()
    manifest=json.loads(args.manifest.read_text())
    reader,mapping,outline=map_pages(args.pdf,manifest)
    report={'pdf':str(args.pdf.resolve()),'pdf_sha256':hashlib.sha256(args.pdf.read_bytes()).hexdigest(),'pages':len(reader.pages),'outline_entries':len(outline),'headings':mapping,'volume':volume_limits(mapping,len(reader.pages))}
    if args.verify:report['toc']=verify_toc(reader,mapping,outline)
    args.output.parent.mkdir(parents=True,exist_ok=True)
    if not args.verify:args.output.write_text(json.dumps(mapping,ensure_ascii=False,indent=2)+'\n')
    (args.output.parent/'pagination-verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
    if args.verify and report['toc']['mismatches']:raise ValueError('TOC mismatch: '+str(report['toc']['mismatches']))
    if args.check_limits and not all(report['volume']['checks'].values()):raise ValueError('Profile GIA volume violation: '+str(report['volume']))
    if args.publish:
        if not args.verify or not args.check_limits:raise ValueError('--publish requires --verify --check-limits')
        (ROOT/'thesis/document-page-map.json').write_text(json.dumps(mapping,ensure_ascii=False,indent=2)+'\n')
        (ROOT/'thesis/document-statistics.json').write_text(json.dumps({'pages':len(reader.pages),**report['volume'],'figures':manifest['figures'],'tables':manifest['tables'],'sources':manifest['sources'],'pdf_sha256':report['pdf_sha256']},ensure_ascii=False,indent=2)+'\n')
    print(json.dumps({'pages':len(reader.pages),'headings':len(mapping),'volume':report['volume'],'toc_verified':args.verify,'toc_mismatches':len(report['toc']['mismatches']) if args.verify else None},ensure_ascii=False))


if __name__=='__main__':main()
