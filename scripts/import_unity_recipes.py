#!/usr/bin/env python3
"""Import pinned MIT Unity API recipes as non-executable, hash-checked reference data."""
import argparse,hashlib,json,pathlib,re,subprocess,uuid
ROOT=pathlib.Path(__file__).resolve().parents[1]
SOURCE=ROOT/'.private/upstream-research/unity-mcp-skills'
REPO='batihandev/unity-mcp-skills'
COMMIT='090c8e3eaefa23d57a98e237572a4ffbd19df444'
LIB=ROOT/'Packages/com.worldweaver.toolkit/Editor/RecipeLibrary'
SKILLS=ROOT/'Packages/com.worldweaver.toolkit/Editor/Skills'
sha=lambda b:hashlib.sha256(b).hexdigest()
def meta(p):
 q=p.with_name(p.name+'.meta')
 if not q.exists():q.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid5(uuid.NAMESPACE_URL,'worldweaver:'+str(p.relative_to(ROOT))).hex+'\n'+('folderAsset: yes\n' if p.is_dir() else ''))
def build():
 assert subprocess.check_output(['git','-C',str(SOURCE),'rev-parse','HEAD'],text=True).strip()==COMMIT
 LIB.mkdir(parents=True,exist_ok=True);meta(LIB)
 records=[];paths={}
 for p in sorted((SOURCE/'recipes').rglob('*.md')):
  if p.name=='README.md':continue
  rel=p.relative_to(SOURCE/'recipes');identifier=re.sub('[^a-z0-9-]','-',str(rel.with_suffix('')).replace('_','-').lower()).strip('-')
  if identifier in paths:raise ValueError('Duplicate recipe ID '+identifier)
  paths[identifier]=str(rel)
  raw=p.read_bytes();text=raw.decode('utf-8-sig');target=LIB/'sources'/rel;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(raw)
  for d in [target.parent,target.parent.parent]:meta(d)
  meta(target)
  lines=[s.strip() for s in text.splitlines() if s.strip()]
  title=next((s.lstrip('# ').strip() for s in lines if s.startswith('# ')),identifier)
  summary=next((s for s in lines[1:] if not s.startswith(('#','**','```','|','-'))),title)
  sig=re.search(r'\*\*Signature:\*\*\s*`([^`]+)`',text)
  records.append({'id':identifier,'domain':rel.parts[0],'title':title,'description':summary[:350],'signature':sig.group(1) if sig else '', 'path':'sources/'+rel.as_posix(),'sha256':sha(raw),'bytes':len(raw),'sourcePath':'recipes/'+rel.as_posix(),'sourceCommit':COMMIT,'license':'MIT','links':re.findall(r'\]\(([^)]+\.md)\)',text)})
 for r in records:
  source=(SOURCE/r['sourcePath']).resolve();r['references']=[]
  for link in r.pop('links'):
   candidate=(source.parent/link).resolve()
   if candidate.is_relative_to(SOURCE/'recipes'):
    rel=candidate.relative_to(SOURCE/'recipes').as_posix();r['references'] += [key for key,value in paths.items() if value==rel]
 (LIB/'catalog.json').write_text(json.dumps({'format':1,'mode':'reference-only','repository':'https://github.com/'+REPO,'commit':COMMIT,'recipes':records},ensure_ascii=False,indent=2)+'\n');meta(LIB/'catalog.json')
 (LIB/'LICENSE.txt').write_bytes((SOURCE/'LICENSE').read_bytes());meta(LIB/'LICENSE.txt')
 catalog=json.loads((SKILLS/'catalog.json').read_text());catalog['skills']=[r for r in catalog['skills'] if r.get('source_group')!='unity-recipes']
 for p in sorted((SOURCE/'skills').glob('*/SKILL.md')):
  domain=p.parent.name;raw=p.read_bytes();text=raw.decode('utf-8-sig');identifier='ww-recipes-'+domain
  desc=re.search(r'description:\s*"?(.*?)(?:"\s*\n|\n---)',text,re.S);desc=re.sub(r'\s+',' ',desc.group(1)).strip(' "') if desc else domain
  desc=('Unity API/C# рецепты: '+desc.replace('<','(').replace('>',')'))[:310]
  mistakes=re.search(r'## Common Mistakes\s*(.*?)(?=\n## |\Z)',text,re.S);notes=(mistakes.group(1).strip() if mistakes else '')[:2200]
  body=f'''---
name: {identifier}
description: {desc}
category: unity-recipes
keywords: {domain} {domain.replace('-', ' ')} {desc}
mode: advisory
tools: find_unity_recipes read_unity_recipe reflect_api execute_editor_code inspect_project
source: https://github.com/{REPO}/blob/{COMMIT}/skills/{domain}/SKILL.md
---
# Unity API reference: {domain}

Find the relevant C# reference with find_unity_recipes, query "{domain} <specific operation>". Read the matching recipe and its returned prerequisite IDs with read_unity_recipe; use nextOffset if a recipe is paged. Inspect actual project packages and API signatures before adapting unfamiliar code.

The source recipes target another agent's IRunCommand/ExecutionResult host. These interfaces and the source MCP tool names are NOT installed WorldWeaver commands. Adapt the useful Unity API method body to our execute_editor_code(GameObject target) wrapper and return measured data; use registered scene tools when they already implement the operation. New runtime components use the existing script compilation workflow. References never grant permission or prove that an operation ran.

## Domain notes from the source (reference data)
{notes or 'Check the selected recipe requirements and the installed Unity/package versions.'}

Source: {REPO}@{COMMIT}; MIT. This guide is an adaptation; the original domain documentation hash is recorded in catalog.json. Recipe source files are kept unchanged in RecipeLibrary.
'''
  out=SKILLS/identifier;out.mkdir(exist_ok=True);meta(out);(out/'SKILL.md').write_text(body);meta(out/'SKILL.md')
  catalog['skills'].append({'id':identifier,'sha256':sha(body.encode()),'source_repo':REPO,'source_commit':COMMIT,'source_path':'skills/'+domain+'/SKILL.md','source_sha256':sha(raw),'license':'MIT','source_group':'unity-recipes'})
 (SKILLS/'catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2)+'\n')
 license=SKILLS/'LICENSES/batihandev-MIT.txt';license.write_bytes((SOURCE/'LICENSE').read_bytes());meta(license)
 lock={'repository':'https://github.com/'+REPO,'commit':COMMIT,'licenseSha256':sha((SOURCE/'LICENSE').read_bytes()),'recipes':len(records),'domains':34,'recipeCatalogSha256':sha((LIB/'catalog.json').read_bytes())}
 (ROOT/'docs/skills/unity-recipes-lock.json').write_text(json.dumps(lock,indent=2)+'\n')
 verify()
def verify():
 lock=json.loads((ROOT/'docs/skills/unity-recipes-lock.json').read_text());assert lock['commit']==COMMIT
 data=json.loads((LIB/'catalog.json').read_text());assert data['commit']==COMMIT and sha((LIB/'catalog.json').read_bytes())==lock['recipeCatalogSha256']
 ids=set()
 for row in data['recipes']:
  assert row['id'] not in ids;ids.add(row['id']);raw=(LIB/row['path']).read_bytes();assert sha(raw)==row['sha256'] and len(raw)==row['bytes']
 for row in json.loads((SKILLS/'catalog.json').read_text())['skills']:
  if row.get('source_group')=='unity-recipes':assert row['source_commit']==COMMIT and sha((SKILLS/row['id']/'SKILL.md').read_bytes())==row['sha256']
 assert sha((LIB/'LICENSE.txt').read_bytes())==lock['licenseSha256']
 print(json.dumps({'verified':True,'recipes':len(ids),'domains':lock['domains'],'sourceCommit':COMMIT}))
if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--build',action='store_true');a=p.parse_args();build() if a.build else verify()
