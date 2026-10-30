"""Build the static table of contents from the latest rendered PDF, then verify it."""
from pathlib import Path
import json,re,sys
from pypdf import PdfReader
root=Path(__file__).resolve().parents[1]
pdf=Path(sys.argv[1])
reader=PdfReader(pdf)
normalize=lambda s:re.sub(r'\s+','',s).replace('\u00ad','')
manifest=json.loads((root/'.private/build/document-manifest.json').read_text())
page_map={}
page_text=[normalize(p.extract_text()) for p in reader.pages]
for title,level in manifest['headings']:
 matches=[i+1 for i,text in enumerate(page_text) if i>=3 and normalize(title) in text]
 if not matches:raise ValueError('Heading not found: '+title)
 page_map[title]=matches[0]
(root/'.private/build/page-map.json').write_text(json.dumps(page_map,ensure_ascii=False,indent=2))
print('PAGE_MAP',len(page_map),'headings;',len(reader.pages),'pages')
for k,v in page_map.items():print(v,k)
