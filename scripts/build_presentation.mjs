import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import crypto from 'node:crypto';
import { pathToFileURL, fileURLToPath } from 'node:url';

// WorldWeaver 1.2: 12 slides for the official GIA recommendation (6–12).
// Old measurements are never used as fallback values for this version.
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const runtime=process.env.WW_ARTIFACT_RUNTIME??path.join(os.homedir(),'.cache/codex-runtimes/codex-primary-runtime/dependencies');
const skill=process.env.WW_PRESENTATIONS_SKILL??path.join(os.homedir(),'.codex/plugins/cache/openai-primary-runtime/presentations/26.1026.10807/skills/presentations');
const out=path.join(root,'.private/presentation-1.2');
const outline=[
 ['Титульный слайд',20],['Задача и цель работы',50],['Сопоставление с аналогами',35],
 ['Компоненты инструментария',45],['Режимы и проверка задачи',50],['Код, компиляция и продолжение',60],
 ['Отбор предметных MD-навыков',35],['Игровой мир и его состояние',50],['Интерфейс и подключение API',35],
 ['Программные испытания версии 1.2',50],['Реальные модельные сценарии',55],['Результат и границы применения',35]
];
if(process.argv.includes('--outline-only')){
 console.log(JSON.stringify({version:'1.2-development',slides:outline.map(([title,seconds],i)=>({number:i+1,title,seconds})),planned_seconds:outline.reduce((n,s)=>n+s[1],0),software_metrics:'pending; no fallback to 1.1'},null,2));
 process.exit(0);
}
const draft=process.argv.includes('--draft');
const evidencePath=process.env.WW_PRESENTATION_EVIDENCE??'results/presentation-evidence-1.2.json';
let evidence,evidenceBytes;
try{evidenceBytes=await fs.readFile(path.resolve(root,evidencePath));evidence=JSON.parse(evidenceBytes.toString());}
catch(error){if(!draft)throw new Error('Missing final 1.2 evidence. Use --draft only for a private layout preview; never substitute old results. '+error.message);evidence={version:'1.2',finalized:false,testRuns:[],modelRuns:[],observations:[],screenshots:{}};}
if(evidence.version!=='1.2'||!draft&&evidence.finalized!==true)throw new Error('Presentation evidence is not finalized for version1.2.');
const digest=bytes=>crypto.createHash('sha256').update(bytes).digest('hex');
const sourceBindings=evidenceBytes?{[evidencePath]:digest(evidenceBytes)}:{};
async function readEvidence(name){const bytes=await fs.readFile(path.resolve(root,name));sourceBindings[name]=digest(bytes);return bytes;}
await readEvidence('scripts/build_presentation.mjs');
await readEvidence('scripts/presentation-evidence-1.2.schema.json');
if(!draft){
 const pkg=JSON.parse((await readEvidence('Packages/com.worldweaver.toolkit/package.json')).toString());
 if(!pkg.version.startsWith('1.2.'))throw new Error('Final deck requires a 1.2 package.');
 if(!/^[a-f0-9]{7,64}$/.test(evidence.sourceCommit??'')||!Object.keys(evidence.sourceHashes??{}).length||!evidence.testRuns?.length||!evidence.modelRuns?.length)throw new Error('Final source identity, software tests and real model protocols are required.');
 for(const [name,expected]of Object.entries(evidence.sourceHashes??{}))if(digest(await readEvidence(name))!==expected)throw new Error('Source changed after evidence capture: '+name);
 for(const key of ['ui','world','api']){
  const item=evidence.screenshots?.[key];if(!item?.path||!item.sha256||!item.caption)throw new Error('Missing reviewed 1.2 screenshot: '+key);
  if(digest(await readEvidence(item.path))!==item.sha256)throw new Error('Screenshot differs from reviewed capture: '+key);
 }
}
const tests=[];
for(const row of evidence.testRuns??[]){
 const xml=(await readEvidence(row.path)).toString();const match=xml.match(/<test-run\b([^>]+)>/);if(!match)throw new Error('Missing NUnit result: '+row.path);
 const a={};for(const x of match[1].matchAll(/([\w-]+)="([^"]*)"/g))a[x[1]]=x[2];
 if(!a['end-time'])throw new Error('Unfinished NUnit run: '+row.path);
 tests.push({...row,total:Number(a.total),passed:Number(a.passed),failed:Number(a.failed),skipped:Number(a.skipped??0),finished:a['end-time']});
}
const checks=[];
for(const row of evidence.checkRuns??[]){
 const bytes=await readEvidence(row.path);
 if(row.sha256&&digest(bytes)!==row.sha256)throw new Error('Check protocol changed after evidence capture: '+row.path);
 const data=JSON.parse(bytes.toString());
 if(!data[row.finishedField??'finishedUtc'])throw new Error('Unfinished check protocol: '+row.path);
 const passed=data[row.passedField??'passedChecks'],total=data[row.totalField??'totalChecks'];
 if(!Number.isInteger(passed)||!Number.isInteger(total)||total<1||passed<0||passed>total)throw new Error('Invalid check counts: '+row.path);
 checks.push({...row,passed,total});
}
const models=[];
for(const row of evidence.modelRuns??[]){
 const bytes=await readEvidence(row.path);
 if(row.sha256&&digest(bytes)!==row.sha256)throw new Error('Model protocol changed after evidence capture: '+row.path);
 const data=JSON.parse(bytes.toString());
 if(!data[row.finishedField??'finishedUtc'])throw new Error('Unfinished model observation: '+row.path);
 const cases=data[row.casesField??'tests'];if(!Array.isArray(cases)||!cases.length)throw new Error('Model case list missing: '+row.path);
 if(!draft&&(!/^[a-f0-9]{7,64}$/.test(row.sourceCommit??'')||!row.sourceSnapshot||!row.sourceTreeSha256))throw new Error('Each model protocol requires its own source identity: '+row.path);
 if(row.sourceSnapshot){
  const snapshot=JSON.parse((await readEvidence(row.sourceSnapshot)).toString());
  if(snapshot.content_tree_sha256!==row.sourceTreeSha256)throw new Error('Model source tree differs from recorded snapshot: '+row.path);
 }
 const passed=cases.filter(v=>v[row.passField??'passed']===true).length;
 models.push({...row,model:data[row.modelField??'modelKey']??row.label,total:cases.length,passed,failed:cases.length-passed});
}
for(const row of evidence.observations??[])await readEvidence(row.path);
if(!draft&&(tests.length+checks.length>5||models.length>3))throw new Error('Keep evidence slide-readable: max5 software runs/max3 model configurations; detail belongs in notes.');
process.env.RUNTIME_NODE_MODULES=path.join(runtime,'node/node_modules');
process.env.RUNTIME_NODE=path.join(runtime,'node/bin/node');
process.env.RUNTIME_PYTHON=path.join(runtime,'python/bin/python3');
await fs.mkdir(out,{recursive:true});
const sources=JSON.parse(await fs.readFile(path.join(root,'docs/research/sources.json'),'utf8'));
const {Presentation,PresentationFile}=await import(pathToFileURL(path.join(runtime,'node/node_modules/@oai/artifact-tool/dist/artifact_tool.mjs')).href);
const {applyPresentationChartFont,finalizePresentation}=await import(pathToFileURL(path.join(skill,'container_tools/artifact_tool_utils.mjs')).href);
const pres=Presentation.create({slideSize:{width:1280,height:720}});
const C = { navy:'#142F46', teal:'#087F86', ink:'#203A50', muted:'#52677A', pale:'#EAF4F3', gray:'#D7E0E5', bg:'#FCFDFC', white:'#FFFFFF', orange:'#C67038' };
const FONT = 'Arial';
const manifest = [];
const evidenceViews = [];
function text(slide, content, x, y, w, h, size=26, opts={}) {
  const s=slide.shapes.add({ geometry:'textbox',name:opts.name??content.slice(0,50),position:{left:x,top:y,width:w,height:h},fill:'none',line:{fill:'none',width:0} });
  s.text=content; s.text.style={typeface:FONT,fontSize:size,color:opts.color??C.ink,bold:opts.bold??false,alignment:opts.align??'left',verticalAlignment:opts.middle?'middle':'top',wrap:'square',insets:0,...opts.style};
  return s;
}
function line(s,x,y,w,h=0,color=C.gray,dash=false) { return s.shapes.add({geometry:'line',position:{left:w<0?x+w:x,top:h<0?y+h:y,width:Math.abs(w),height:Math.abs(h),verticalFlip:h<0,horizontalFlip:w<0},fill:'none',line:{fill:color,width:1.5,style:dash?'dashed':'solid'}}); }
function shape(s,label,x,y,w,h,opts={}) {
  const r=s.shapes.add({geometry:opts.geometry??'rect',name:label,position:{left:x,top:y,width:w,height:h},fill:opts.fill??C.white,line:{fill:opts.stroke??C.teal,width:1.5}});
  if(label) { r.text=label;r.text.style={typeface:FONT,fontSize:opts.size??24,color:opts.color??C.ink,bold:opts.bold??false,alignment:'center',verticalAlignment:'middle',insets:10}; }
  return r;
}
function connect(s,a,b,from='right',to='left',opts={}) { return s.shapes.connect(a,b,{kind:opts.kind??'straight',fromSide:from,toSide:to,line:{fill:opts.color??C.teal,width:1.7,style:opts.dashed?'dashed':'solid'},tail:{type:'arrow',width:'med',length:'med'},...(opts.bidirectional?{head:{type:'arrow',width:'med',length:'med'}}:{})}); }
function arrow(s,x1,y1,x2,y2,label='',opts={}) {
  s.shapes.add({geometry:'connector',kind:'straight',position:{left:Math.min(x1,x2),top:Math.min(y1,y2),width:Math.abs(x2-x1),height:Math.abs(y2-y1)},line:{fill:opts.color??C.teal,width:1.7,style:opts.dashed?'dashed':'solid'},...((x2>x1||(x2===x1&&y2>=y1))?{tail:{type:'arrow'}}:{head:{type:'arrow'}})});
  if(label)text(s,label,Math.min(x1,x2)+8,Math.min(y1,y2)-28,Math.max(120,Math.abs(x2-x1)-16),28,21,{color:opts.color??C.ink});
}
function slide(title, section, notes='', refs=[]) {
  const s=pres.slides.add();s.background.fill=C.bg;
  const n=pres.slides.items.length;
  text(s,section.toUpperCase(),64,30,1060,24,17,{color:C.teal,bold:true});
  text(s,title,64,75,1152,76,42,{bold:true,color:C.navy,name:'slide-title'});
  line(s,64,156,1152);
  text(s,'WorldWeaver 1.2 · ВолгГТУ',64,678,800,21,16,{color:C.muted});
  text(s,String(n).padStart(2,'0'),1150,676,66,24,18,{color:C.muted,align:'right'});
  s.speakerNotes.text=notes+'\n\nИсточники:\n'+refs.map(id=>{const d=sources.find(v=>v.id===id);return d?`[${id}] ${d.title}: ${d.url}`:String(id)}).join('\n');
  manifest.push({slide:n,title,section});return s;
}
function rows(s,items,x=64,y=195,w=1152,gap=94,size=27) {
  items.forEach((v,i)=>{text(s,String(i+1).padStart(2,'0'),x,y+i*gap,58,40,26,{color:C.teal,bold:true});text(s,v,x+82,y+i*gap,w-82,gap-12,size);});
}
function table(s,values,y,height,widths,size=23,x=64,w=1152) {
  const t=s.tables.add({rows:values.length,columns:values[0].length,left:x,top:y,width:w,height,columnWidths:widths,values});
  t.borders.assign({fill:C.gray,width:1,style:'solid'});
  t.cells.block({row:0,column:0,rowCount:values.length,columnCount:values[0].length}).assign({fill:C.white,textStyle:{typeface:FONT,fontSize:size,color:C.ink},margins:{left:14,right:14,top:8,bottom:6},anchor:'center'});
  t.cells.block({row:0,column:0,rowCount:1,columnCount:values[0].length}).assign({fill:C.navy,textStyle:{typeface:FONT,fontSize:size,bold:true,color:C.white}});
  for(let r=1;r<values.length;r++)if(r%2===0)t.cells.block({row:r,column:0,rowCount:1,columnCount:values[0].length}).fill=C.pale;
  return t;
}
async function evidenceImage(s,key,x,y,w,h,viewport=null) {
 const item=evidence.screenshots?.[key];
 if(!item){text(s,'Фактический снимок версии 1.2\nожидает подтверждения',x,y,w,h,28,{color:C.muted,middle:true});return;}
 const bytes=await readEvidence(item.path);
 // Native capture may retain a .png filename while containing JPEG bytes.
 // Detect the original encoding; never re-encode or modify evidence images.
 const contentType=bytes.subarray(0,8).equals(Buffer.from([137,80,78,71,13,10,26,10]))?'image/png':bytes[0]===255&&bytes[1]===216?'image/jpeg':null;
 if(!contentType)throw new Error('Unsupported evidence image signature: '+item.path);
 if(item.sha256&&digest(bytes)!==item.sha256)throw new Error('Screenshot changed before embedding: '+item.path);
 let crop;
 if(viewport){
  const {sourceWidth,sourceHeight,left,top,width,height}=viewport;
  if(![sourceWidth,sourceHeight,width,height].every(n=>Number.isFinite(n)&&n>0)||![left,top].every(n=>Number.isFinite(n)&&n>=0)||left+width>sourceWidth||top+height>sourceHeight)throw new Error('Invalid evidence viewport: '+key);
  crop={left:left/sourceWidth,top:top/sourceHeight,right:(sourceWidth-left-width)/sourceWidth,bottom:(sourceHeight-top-height)/sourceHeight};
  // Match the viewport aspect exactly so cover cannot remove more evidence.
  const fittedHeight=w*height/width;if(fittedHeight>h+0.5)throw new Error('Evidence viewport does not fit its allocated frame: '+key);
  h=fittedHeight;
 }
 const frame={left:x,top:y,width:w,height:h};
 s.images.add({blob:bytes.buffer.slice(bytes.byteOffset,bytes.byteOffset+bytes.byteLength),contentType,alt:item.caption,fit:crop?'cover':'contain',position:frame,...(crop?{crop}:{})});
 evidenceViews.push({slide:manifest.length,key,path:item.path,source_sha256:digest(bytes),contentType,frame,viewport,crop:crop??null,original_bytes_supplied:true});
}
function chart(s,config) { const c=s.charts.add('bar',config);applyPresentationChartFont(c,{fontFamily:FONT});return c; }

function projectNotes(...names){return '\nИсходники проекта:\n'+names.map(n=>path.resolve(root,n)).join('\n');}
function diagramNode(s,label,x,y,w=280,h=72){return shape(s,label,x,y,w,h,{size:25});}
function pendingNote(s){text(s,'Испытания версии 1.2 ещё не завершены',64,634,1152,30,22,{color:C.orange});}
// 01: approved subject, unchanged.
{
 const s=pres.slides.add();s.background.fill=C.navy;
 text(s,'ВОЛГОГРАДСКИЙ ГОСУДАРСТВЕННЫЙ\nТЕХНИЧЕСКИЙ УНИВЕРСИТЕТ',64,42,1120,60,22,{color:C.white});
 text(s,'Разработка интеллектуального\nинструментария для автоматизированного\nсоздания динамических игровых миров',64,193,1152,250,48,{color:C.white,bold:true,name:'slide-title'});
 text(s,'Выпускная квалификационная работа',64,473,1080,36,26,{color:'#B9DCDD'});
 text(s,'Коваленко Кирилл Дмитриевич\nИВТ-465 · 09.03.01 Информатика и вычислительная техника',64,550,1140,76,25,{color:C.white});
 text(s,'ФЭВТ · кафедра САПР и ПК · Волгоград, 2027',64,664,1120,25,19,{color:'#B9DCDD'});
 s.speakerNotes.text='Исходная тема сохранена. Неизвестный руководитель не выдуман. Год 2027 сохранён из рабочего титула и не устанавливается опубликованной программой ГИА. Текущая разработка1.2 описывается отдельно от старых измерений.\n'+projectNotes('WORKPLAN-1.2.md','docs/research/vkr-requirements-2026.md');
 manifest.push({slide:1,title:outline[0][0],section:'Введение'});
}
// 02
{
 const s=slide(outline[1][0],'Постановка задачи','Цель сохраняет тему ВКР. Проект автоматизирует ограниченный процесс authoring в Unity, а результат проверяет через живую сцену, компилятор и тестовые задания. Полное выполнение произвольной разработки не обещается.'+projectNotes('WORKPLAN-1.2.md','Packages/com.worldweaver.toolkit/Editor/Assistant/AgentTasks.cs'));
 text(s,'Создать инструментарий Unity\nдля диалогового построения игрового мира\nи проверяемого изменения проекта',64,190,1152,148,36,{bold:true,color:C.navy});
 rows(s,['Связать сцену, поведение, знания NPC и состояние игры.','Ограничить действия режимом, контрактом и просмотром кода.','Проверять результат по наблюдаемому состоянию Unity.'],64,380,1152,90,28);
}
// 03 native comparison, no unsupported superiority claim.
{
 const s=slide(outline[2][0],'Анализ решений','Сравнение функциональных областей по первичным источникам. Unity Assistant уже поддерживает агентные сценарии; очное испытание одного протокола не выполнялось. Inworld рассматривается как runtime SDK, прежний Character Studio закрыт.'+projectNotes('docs/research/chat-agent-comparison.md'),[5,6,7,10,11,12,13]);
 table(s,[['Решение','Основная область','Связь с задачей'],['Unity AI / Assistant','Агентные действия в Editor','Изменения и инструменты Unity'],['Convai / Inworld','Диалог и действия NPC','NPC и индивидуальные знания'],['Gaia','Создание игрового окружения','Terrain и размещение объектов'],['LLMUnity','Локальные модели в Unity','Локальное выполнение LLM'],['WorldWeaver','Мир и агент в одном пакете','Мир, C#, NPC и состояние']],188,430,[325,390,437],24);
 text(s,'Сравнение определяет область реализации и ограничения',64,624,1152,34,25,{color:C.teal,bold:true});
}
// 04 editable component diagram.
{
 const s=slide(outline[3][0],'Проектирование','Логические подсистемы соответствуют текущим C# классам. Контекст: ProjectContextIndex/AgentSkillCatalog/AgentDesignMemory. Task сохраняется в сообщении через ChatHistoryStore. Verifier проверяет только извлечённые условия; Resume работает в пределах бюджета. Runtime NPC/RAG и игровые системы остаются отдельным контуром.'+projectNotes('docs/uml/component.puml','docs/uml/architecture-1.2-notes.md'));
 const ui=diagramNode(s,'Чат Unity Editor',64,189,290,64);
 const agent=diagramNode(s,'ChatController\nрежим и задача',484,189,310,76);
 const context=diagramNode(s,'Контекст проекта\nMD-навыки и история',936,189,280,76);
 const checkpoint=diagramNode(s,'Checkpoint\nдо изменений',484,339,310,76);
 const scene=diagramNode(s,'Сцена и игровой мир',64,492,290,76);
 const code=diagramNode(s,'Просмотр C#\ncompiler / jobs',484,492,310,76);
 const verify=diagramNode(s,'Проверка результата\nи продолжение',936,492,280,76);
 connect(s,ui,agent);connect(s,agent,context);connect(s,agent,checkpoint,'bottom','top');
 connect(s,checkpoint,scene,'left','top');connect(s,checkpoint,code,'bottom','top');connect(s,code,verify);
 connect(s,scene,verify,'bottom','bottom',{kind:'elbow'});
 text(s,'Runtime: NPC, RAG, NavMesh, игрок, квест и Save/Load',64,617,1152,36,26,{color:C.teal});
}
// 05 native mode table, finite conditions rather than universal intent understanding.
{
 const s=slide(outline[4][0],'Выполнение задачи','Ask ограничивает подмножество schema и guard чтением. Plan может читать и предлагает изменяющий PendingPlan. Execute выполняет разрешённые инструменты; новый код и определённые операции сохраняют просмотр. AgentTaskVerifier извлекает конечные RU/EN literal patterns. Проверка компонентов игрока/квеста не равна прохождению игры.'+projectNotes('Packages/com.worldweaver.toolkit/Editor/Assistant/ChatController.Agent.cs','Packages/com.worldweaver.toolkit/Editor/Assistant/AgentTasks.cs'));
 table(s,[['Режим','Что допускает','Что видит разработчик'],['Спросить / Ask','Чтение проекта и ответ','Объяснение без изменения сцены'],['Спланировать / Plan','Чтение и предложение изменений','План для явного применения'],['Выполнить / Execute','Разрешённые изменения','Фактические результаты инструментов']],190,300,[285,415,452],25);
 text(s,'Запрос сохраняет явные условия: количество, размеры, цели',64,508,1152,40,28,{bold:true,color:C.navy});
 text(s,'Unity даёт данные проверки. Оставшиеся нарушения видны в отчёте.',64,580,1152,67,27);
}
// 06 editable sequence with actual lifecycle boundaries.
{
 const s=slide(outline[5][0],'Реализация','Рассматривается текущий workflow1.2. Просмотр фиксирует область файлов. Checkpoint сохраняет состояние до изменений; ScriptDrafts/AgentSourcePatches получают compiler feedback. Task/Jobs сохраняют ожидание. GUI ResumeCoordinator ждёт окончания compiler/import/Play/job; до3продолжений, малые compile repairs на первых2в просмотренной области. Потерянный job после рестарта не обещан к автоматическому перезапуску. Компиляция не является проверкой всех игровых свойств. Отдельный actual Play Mode measurement точного просмотренного R2черновика наR3:60.0680817453° за2.0022588648с, off-axis0,99samples. Неподвижный контроль99samples дал0° и корректно не прошёл; эти samples не независимые model cases.'+projectNotes('docs/uml/sequence.puml','Packages/com.worldweaver.toolkit/Editor/Assistant/AgentTaskResumeCoordinator.cs',...(evidence.runtimeProof?.path?[evidence.runtimeProof.path,'results/code-and-runtime-source-1.2.json']:[])));
 const xs=[64,385,706,1015],ws=[230,230,230,201];
 ['Разработчик','Контроллер','Unity C# / tests','Продолжение'].forEach((label,i)=>{diagramNode(s,label,xs[i],190,ws[i],55);line(s,xs[i]+ws[i]/2,254,0,325,C.gray,true);});
 arrow(s,500,281,179,281,'Код / diff');
 arrow(s,179,342,500,342,'Применить');
 arrow(s,500,403,821,403,'Checkpoint и импорт');
 arrow(s,821,464,1115,464,'Compiler / reload');
 arrow(s,1115,539,500,539,'Результат и Resume');
 text(s,evidence.runtimeCaption??'В пределах бюджета: исправление, новая проверка или честный итог',64,607,1152,40,25,{color:C.teal,bold:true});
}
// 07 native evidence table + process diagram, not a decorative card dashboard.
{
 const s=slide(outline[6][0],'Предметные знания агента','AgentSkillCatalog читает bounded UTF8 MD с metadata и catalog hashes. Selection лексический, до4 навыков при текущем вызове, в ограниченном символьном бюджете; каталог не добавляет tools. Внешние пользовательские MD считаются advisory. Число файлов не используется как доказательство успешности агента.'+projectNotes('Packages/com.worldweaver.toolkit/Editor/Assistant/AgentSkillCatalog.cs','Packages/com.worldweaver.toolkit/Editor/Skills/catalog.json'));
 const a=diagramNode(s,'Запрос задачи',64,195,290,72),b=diagramNode(s,'Поиск по метаданным',474,195,310,72),c=diagramNode(s,'Выбранный MD-контекст',914,195,302,72);
 connect(s,a,b);connect(s,b,c);
 table(s,[['Слой','Сведения и проверка'],['Каталог','Источник, лицензия и SHA в описании навыка'],['Отбор','Совпадение с запросом и размер контекста'],['Исполнение','Контракт, разрешения и отдельный просмотр C#']],335,258,[310,842],26);
 text(s,'Текст навыка служит справкой и не предоставляет новые разрешения',64,621,1152,34,25,{color:C.teal,bold:true});
}
// 08 actual 1.2 Play capture plus editable observed gameplay sequence.
{
 const s=slide(outline[7][0],'Игровой runtime','WorldGameplayTools реализует create_village: площадка, именованные места, дома/входы/дороги/north_gate, guard и NavMesh; set_navigation проверяет полный маршрут и возврат. Показан подлинный GUI Play Mode заранее заданной деревни, без LLM: E у жителя дал письмо1, WASD и E у стража завершили доставку и дали5монет; повторное E не дало повторной награды. Отдельно действительно запущен standalone Development Build macOS и клавиатурой завершён тот же bounded quest с5монетами и без второй награды. HUD mouse save/load и time buttons этим smoke не подтверждены. Это ручные наблюдения игрового цикла, не качество генерации мира, оценка общей надёжности или проверка других платформ. WorldSaveState и scoped NPC memory имеют отдельные проверки.'+projectNotes('Packages/com.worldweaver.toolkit/Editor/Assistant/WorldGameplayTools.cs','Packages/com.worldweaver.toolkit/Runtime/World/WorldSaveState.cs','results/ui-centering-1.2-native-verification.json','results/player-build-verification.json','results/player-native-smoke-1.2.json'));
 text(s,'Деревня, игрок, NPC и доставка письма',64,180,780,34,27,{bold:true,color:C.navy});
 // Original2900x1676JPEG: source HUD + guard torso, without Editor chrome.
 await evidenceImage(s,'world',64,225,780,430,{sourceWidth:2900,sourceHeight:1676,left:500,top:230,width:1150,height:630});
 const letter=diagramNode(s,'Получить письмо',900,223,316,68),guard=diagramNode(s,'Доставить стражу',900,359,316,68),reward=diagramNode(s,'Награда: 5 монет',900,495,316,68);
 connect(s,letter,guard,'bottom','top');connect(s,guard,reward,'bottom','top');
 text(s,'Preset без LLM\nПовтор E: без новой награды',900,596,316,64,23,{color:C.teal});
}
// 09 actual user interface and explicit API connection, no inference claim.
{
 const s=slide(outline[8][0],'Работа разработчика','Два подлинных снимка1.2: чат с общей осью карточек и композера; форма добавления собственной модели. В native UI действительно добавлен OpenAI-compatible Ollama /v1 с qwen3.5:9b, без ключа; GET-проверка нашла точную модель, completion не запускался. Это не проверка качества LLM или всех внешних сервисов. Отдельные14 mock HTTP/actual reload checks проверяют адаптер и жизненный цикл, а не вывод модели. OpenAI-compatible маршрут текстовый; loopback Ollama vision только после подтверждения capability. Ключ формы живёт в окружении процесса Unity, в несекретной записи остаётся имя переменной. История и ожидание задачи отдельно от игрового Save/Load.'+projectNotes('Packages/com.worldweaver.toolkit/Editor/Assistant/WorldWeaverChatWindow.cs','Packages/com.worldweaver.toolkit/Editor/Assistant/ChatModelConnections.cs','Packages/com.worldweaver.toolkit/Editor/Assistant/ChatModelConnectionWindow.cs','results/ui-centering-1.2-native-verification.json','results/custom-model-connections-verification.json','docs/custom-api.md'));
 text(s,'Выбор модели',64,183,600,34,27,{bold:true,color:C.navy});
 await evidenceImage(s,'ui',64,228,600,100,{sourceWidth:2940,sourceHeight:1716,left:40,top:205,width:755,height:120});
 text(s,'Режим задачи',64,366,600,34,27,{bold:true,color:C.navy});
 await evidenceImage(s,'ui',64,413,600,100,{sourceWidth:2940,sourceHeight:1716,left:595,top:1500,width:610,height:86});
 text(s,'Контекст, шаги и история\nсохраняются с задачей',64,557,600,80,26,{color:C.muted});
 text(s,'Добавить API без C#',716,183,500,34,27,{bold:true,color:C.navy});
 await evidenceImage(s,'api',716,228,500,85,{sourceWidth:1000,sourceHeight:1276,left:300,top:204,width:695,height:116});
 await evidenceImage(s,'api',716,330,500,60,{sourceWidth:1000,sourceHeight:1276,left:300,top:364,width:695,height:80});
 text(s,'OpenAI-compatible / Ollama',716,441,500,40,26,{bold:true,color:C.navy});
 text(s,'URL, точный ID и формат ответа\nКлюч текущего процесса',716,502,500,85,26);
 text(s,'На снимке: GET-проверка без генерации',716,616,500,45,22,{color:C.muted});
}
// 10 current software checks only.
{
 const s=slide(outline[9][0],'Верификация','Числа читаются из конечных NUnit XML и отдельных JSON-протоколов программных проверок, указанных в evidence manifest. Разные типы checks не складываются в общую accuracy модели. Исторические108/3/11/34 версии1.1 в этой таблице не используются. C#11/11 версии1.2 относится к точному новому черновику изR2беседы, испытанному наR3; это отдельный protocol compile/attach/revert, не проверка скорости вращения.'+projectNotes(...(evidence.testRuns??[]).map(r=>r.path),...(evidence.checkRuns??[]).map(r=>r.path),...(evidence.observations??[]).map(r=>r.path)));
 const data=tests.length?[...tests,...checks].map(r=>[r.label,`${r.passed}/${r.total}`,r.scope??'Программный инвариант']):[['EditMode','Ожидается','Контракты, режимы, checkpoint, инструменты'],['PlayMode','Ожидается','Игровые механики, навигация, Save/Load'],['C# / Editor jobs','Ожидается','Реальная компиляция, reload и продолжение']];
 const tableH=data.length>4?426:365;
 table(s,[['Протокол','Пройдено','Предмет проверки'],...data],190,tableH,[260,175,717],24);
 if(evidence.finalized)text(s,(evidence.testCaption??'Поведение агента проверяется отдельными реальными запросами'),64,data.length>4?630:589,1152,data.length>4?34:60,26,{color:C.muted});
 else pendingNote(s);
}
// 11 native chart only from finished current real-model protocols.
{
 const s=slide(outline[10][0],'Наблюдения с моделями','Доли получены из finished JSON и сохранены без переклассификации FAIL. Последовательные запросы могут зависеть друг от друга; одна серия не оценивает общую вероятность успеха. Исторические NPC/RAG90 и чаты1.1 не переатрибутируются1.2. Каждый модельный прогон имеет собственный sourceCommit/snapshot, который может предшествовать финальным программным исправлениям; результат не переатрибутируется позднему коду. Черновик C# в беседе только предложен для просмотра; compiler/runtime проверяются отдельными протоколами.\n'+models.map(m=>`${m.label}: ${m.sourceCommit??'pending'}; tree${m.sourceTreeSha256??'pending'}`).join('\n')+projectNotes(...(evidence.modelRuns??[]).flatMap(r=>[r.path,...(r.sourceSnapshot?[r.sourceSnapshot]:[])])));
 if(models.length){
  chart(s,{position:{left:64,top:209,width:760,height:370},categories:models.map(m=>`${m.label}, n=${m.total}`),series:[{name:'Пройдено заданий',values:models.map(m=>m.passed),fill:C.teal}],hasLegend:false,barOptions:{direction:'column',grouping:'clustered',gapWidth:110},yAxis:{min:0,max:Math.max(...models.map(m=>m.total))+1,majorUnit:1,numberFormatCode:'0',textStyle:{fontSize:22,fill:C.muted},majorGridlines:{fill:C.gray,width:1}},xAxis:{textStyle:{fontSize:23,fill:C.ink}},dataLabels:{showValue:true,position:'outEnd',numberFormatCode:'0',textStyle:{fontSize:28,bold:true,fill:C.navy}},chartFill:C.bg,plotAreaFill:C.bg});
  text(s,evidence.modelCaption??'Ограниченные реальные сценарии.\nДоли не являются рейтингом моделей.',870,219,346,evidence.visionSummary?190:350,27);
  if(evidence.visionSummary)text(s,evidence.visionSummary,870,430,346,160,25,{color:C.orange});
 }else{
  text(s,'Итоговые реальные сценарии версии 1.2\nожидают завершения и проверки протоколов',64,223,1152,150,36,{bold:true,color:C.navy});
  text(s,'Наблюдаемые действия: мир, навигация, квест, C# и восстановление',64,443,1152,80,28);
 }
 text(s,evidence.modelLimitation??'Результат относится к указанной версии, входам и условиям опыта',64,621,1152,36,24,{color:C.muted});
}
// 12 results correspond to actual final acceptance rather than roadmap promises.
{
 const s=slide(outline[11][0],'Заключение','Реализованные границы следуют исходникам и итоговым протоколам1.2. Исторические измерения сохранены отдельно. Полная замена разработчика, преимущество над Unity Assistant, безошибочное выполнение любой задачи и sandbox произвольного C# не подтверждаются. Перед официальной подачей остаются административные документы и подлинная справка о заимствованиях.'+projectNotes('WORKPLAN-1.2.md','docs/research/vkr-requirements-2026.md',evidencePath));
 text(s,'Единый управляемый процесс\nсоздания и изменения игрового мира в Unity',64,190,1152,110,36,{bold:true,color:C.navy});
 rows(s,evidence.outcomes??['Контекст задачи связан с инструментами и наблюдаемым результатом.','Мир объединяет NPC, навигацию, игровые действия и состояние.','Разработчик сохраняет контроль просмотра C# и восстановления.'],64,355,1152,88,27);
 text(s,evidence.conclusionLimitation??'Границы: модель, бюджет задачи, конечные проверки и ресурсы компьютера',64,631,1152,34,24,{color:C.muted});
}
if(pres.slides.items.length!==12)throw new Error('The1.2 deck must contain exactly12 slides.');
for(let i=0;i<manifest.length;i++)manifest[i].planned_seconds=outline[i][1];
await fs.writeFile(path.join(out,'manifest.json'),JSON.stringify({version:'1.2',draft:!evidence.finalized,slides:manifest,evidence_views:evidenceViews,source_bindings:sourceBindings},null,2));
const candidate=path.join(out,'candidate.pptx');
await(await PresentationFile.exportPptx(pres)).save(candidate);
if(process.argv.includes('--render')){
 for(let i=0;i<pres.slides.items.length;i++){
  const png=await pres.export({slide:pres.slides.items[i],format:'png',scale:1});await fs.writeFile(path.join(out,`slide-${String(i+1).padStart(2,'0')}.png`),new Uint8Array(await png.arrayBuffer()));
 }
 const montage=await pres.export({format:'webp',montage:true});await fs.writeFile(path.join(out,'montage.webp'),new Uint8Array(await montage.arrayBuffer()));
}
if(process.argv.includes('--final')){
 if(draft||evidence.finalized!==true)throw new Error('A draft cannot be finalized.');
 const revision=process.env.WW_PRESENTATION_REVISION??new Date().toISOString().replace(/[:.]/g,'-');
 const finalPath=path.join(out,'validated',`WorldWeaver-1.2-${revision}.pptx`);await fs.mkdir(path.dirname(finalPath),{recursive:true});
 const requiredTables=[3,5,7,10];
 const result=await finalizePresentation({workspaceDir:root,candidatePath:candidate,finalPath,pythonExecutable:path.join(runtime,'python/bin/python3'),integrityValidatorPath:path.join(skill,'container_tools/inspect_presentation_package_integrity.py'),layoutValidatorPath:path.join(skill,'container_tools/inspect_presentation_layout_geometry.py'),layoutArgs:['--expected-slide-size-emu','12192000,6858000','--validate-bullet-geometry','--validate-heading-fit',...requiredTables.flatMap(n=>['--require-native-table-slide',String(n)])],explicitTotalSlideCount:12,requiredNativeTableOwnerSlides:requiredTables,requiredNativeChartOwnerSlides:models.length?[11]:[],materializeLiteralChartWorkbooks:true,fontPolicy:{basis:'design',families:[FONT]},verifyArtifactToolImport:true,receiptPath:path.join(out,`validation-${revision}.json`)});
 console.log(JSON.stringify(result));
}
console.log(JSON.stringify({slides:pres.slides.items.length,candidate,draft:!evidence.finalized,canonical_file_overwritten:false}));
