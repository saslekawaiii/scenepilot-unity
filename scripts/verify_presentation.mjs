import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import crypto from 'node:crypto';
import {pathToFileURL,fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const modules=path.join(process.env.WW_ARTIFACT_RUNTIME ?? path.join(os.homedir(), '.cache/codex-runtimes/codex-primary-runtime/dependencies'), 'node/node_modules');
const {FileBlob,PresentationFile}=await import(pathToFileURL(path.join(modules,'@oai/artifact-tool/dist/artifact_tool.mjs')).href);
const source=path.resolve(process.argv[2]??path.join(root,'deliverables/Презентация_WorldWeaver_финал.pptx'));
const dest=path.resolve(process.argv[3]??path.join(root,'.private/presentation-final-render'));
await fs.mkdir(dest,{recursive:true});
const p=await PresentationFile.importPptx(await FileBlob.load(source));
for(let i=0;i<p.slides.items.length;i++){
 const png=await p.export({slide:p.slides.items[i],format:'png',scale:1});
 await fs.writeFile(path.join(dest,`slide-${String(i+1).padStart(2,'0')}.png`),new Uint8Array(await png.arrayBuffer()));
}
const montage=await p.export({format:'webp',montage:true});
await fs.writeFile(path.join(dest,'montage.webp'),new Uint8Array(await montage.arrayBuffer()));
const digest=bytes=>crypto.createHash('sha256').update(bytes).digest('hex');
const pngs=[];
for(let i=0;i<p.slides.items.length;i++){const file=`slide-${String(i+1).padStart(2,'0')}.png`;pngs.push({slide:i+1,file,sha256:digest(await fs.readFile(path.join(dest,file)))});}
await fs.writeFile(path.join(dest,'source.json'),JSON.stringify({source,source_sha256:digest(await fs.readFile(source)),slides:p.slides.items.length,renderer:'Artifact Tool import + PNG export',powerpoint_inspected:false,pngs},null,2));
console.log(`Imported and rendered ${p.slides.items.length} final slides`);
