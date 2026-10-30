import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import { pathToFileURL, fileURLToPath } from 'node:url';

// Authoring source. Literal data charts deliberately retain editable workbook snapshots.
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const runtime = process.env.WW_ARTIFACT_RUNTIME ?? path.join(os.homedir(), '.cache/codex-runtimes/codex-primary-runtime/dependencies');
process.env.RUNTIME_NODE_MODULES = path.join(runtime, 'node/node_modules');
process.env.RUNTIME_NODE = path.join(runtime, 'node/bin/node');
process.env.RUNTIME_PYTHON = path.join(runtime, 'python/bin/python3');
const skill = process.env.WW_PRESENTATIONS_SKILL ?? path.join(os.homedir(), '.codex/plugins/cache/openai-primary-runtime/presentations/26.1022.11436/skills/presentations');
const { Presentation, PresentationFile } = await import(pathToFileURL(path.join(runtime, 'node/node_modules/@oai/artifact-tool/dist/artifact_tool.mjs')).href);
const { applyPresentationChartFont, finalizePresentation } = await import(pathToFileURL(path.join(skill, 'container_tools/artifact_tool_utils.mjs')).href);
const out = path.join(root, '.private/presentation');
await fs.mkdir(out, { recursive: true });
await fs.mkdir(path.join(root, 'deliverables'), { recursive: true });
const summary = JSON.parse(await fs.readFile(path.join(root, 'results/summary.json'), 'utf8'));
const sources = JSON.parse(await fs.readFile(path.join(root, 'docs/research/sources.json'), 'utf8'));
const local = JSON.parse(await fs.readFile(path.join(root, 'results/end-to-end-local.json'), 'utf8'));
const cloud = JSON.parse(await fs.readFile(path.join(root, 'results/end-to-end-cloud.json'), 'utf8'));
const pres = Presentation.create({ slideSize: { width: 1280, height: 720 } });
const C = { navy:'#142F46', teal:'#087F86', ink:'#203A50', muted:'#52677A', pale:'#EAF4F3', gray:'#D7E0E5', bg:'#FCFDFC', white:'#FFFFFF', orange:'#C67038' };
const FONT = 'Arial';
const manifest = [];
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
  text(s,'WorldWeaver · ВолгГТУ',64,678,800,21,16,{color:C.muted});
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
async function image(s,name,x,y,w,h,crop=undefined){const p=path.join(root,'docs/assets/screenshots',name);const b=await fs.readFile(p);s.images.add({blob:b.buffer.slice(b.byteOffset,b.byteOffset+b.byteLength),contentType:'image/png',alt:`Фактический интерфейс WorldWeaver: ${name}`,fit:'contain',...(crop?{crop}:{}),position:{left:x,top:y,width:w,height:h}});}
function chart(s,config) { const c=s.charts.add('bar',config);applyPresentationChartFont(c,{fontFamily:FONT});return c; }
function ru(v,d=1){return Number(v).toFixed(d).replace('.',',');}
async function xmlCounts(name){const x=await fs.readFile(path.join(root,'results',name),'utf8');const m=x.match(/<test-run\b([^>]+)>/);const a={};for(const t of m[1].matchAll(/([\w-]+)="([^"]*)"/g))a[t[1]]=t[2];return `${a.passed}/${a.total}`;}
function endValue(o,...keys){for(const k of keys)if(o[k]!==undefined)return o[k];return undefined;}

// 01. The full approved subject remains unchanged.
{
 const s=pres.slides.add();s.background.fill=C.navy;
 text(s,'ВОЛГОГРАДСКИЙ ГОСУДАРСТВЕННЫЙ\nТЕХНИЧЕСКИЙ УНИВЕРСИТЕТ',64,42,1120,60,22,{color:C.white});
 // The title is intentionally broken into semantic lines, with no abbreviated topic.
 text(s,'Разработка интеллектуального\nинструментария для автоматизированного\nсоздания динамических игровых миров',64,194,1152,250,48,{color:C.white,bold:true,name:'slide-title'});
 text(s,'Выпускная квалификационная работа',64,473,1080,36,26,{color:'#B9DCDD'});
 text(s,'Коваленко Кирилл Дмитриевич\nИВТ-465 · 09.03.01 Информатика и вычислительная техника',64,550,1140,76,25,{color:C.white});
 text(s,'ФЭВТ · кафедра САПР и ПК · Волгоград, 2027',64,664,1120,25,19,{color:'#B9DCDD'});
 s.speakerNotes.text='Тема сохранена по исходному техническому заданию от 30.09.2026. Профиль: «Разработка игровых и XR приложений». ФИО руководителя в найденных материалах не установлено. Его имя не подставлено предположительно. Год 2027 соответствует найденному плану защиты.\nИсточники: локальное ТЗ и учебные документы автора.';
 manifest.push({slide:1,title:'Титульный слайд',section:'Введение'});
}
{
 const s=slide('Проблема согласованного игрового мира','Актуальность','LLM, RAG и процедурная генерация уже существуют. Задача работы состоит в интеграции этих механизмов в Unity и контроле перехода от вероятностного ответа к игровому состоянию. Не заявляется первенство локальных NPC или RAG.',[14,15,6,11]);
 text(s,'Свободный ответ модели ещё не задаёт допустимое изменение сцены',64,195,1120,88,36,{bold:true,color:C.navy});
 rows(s,['Процедурный рельеф должен повторяться по одной спецификации и seed.','Персонаж должен опираться на собственные знания и текущее состояние.','Передачу предметов и движение должны разрешать правила Unity.'],64,326,1152,100,28);
}
{
 const s=slide('Цель и задачи работы','Постановка задачи','Цель: разработать переносимый инструментарий Unity, который объединяет генерацию Terrain, контекстный диалог и проверяемые действия NPC. Практический вклад оценивается по исходному коду, протоколам и повторяемой демонстрации.');
 text(s,'Разработать подключаемый инструментарий Unity\nдля создания рельефа и контекстно-зависимых NPC',64,195,1130,95,34,{bold:true,color:C.navy});
 rows(s,['Спроектировать общие контракты и маршруты локальных и облачных моделей.','Реализовать TerrainSpec, индивидуальный RAG и исполнитель действий.','Проверить программные инварианты и измерить качество реальных ответов.'],64,340,1152,93,27);
}
{
 const s=slide('План решения','Организация работы','План показывает логические этапы и соответствующие проверяемые результаты. Он не подменяет административный календарный план, который приведён в техническом задании.');
 table(s,[['Этап','Проверяемый результат'],['Анализ','Сравнение аналогов и границы решения'],['Проектирование','ТЗ, архитектура, UML и JSON-контракты'],['Реализация','UPM-пакет, Editor и демонстрационная сцена'],['Проверка','Unity tests, реальные traces и 90 QA-запросов'],['Оформление','Пояснительная записка и материалы защиты']],190,418,[270,882],25);
}
{
 const s=slide('Сопоставление с существующими решениями','Анализ аналогов','Сравнение основано на первичных источниках. Коммерческие продукты не подвергались одинаковому нагрузочному испытанию. Неустановленная возможность не считается отсутствующей. Unity AI Gateway допускает сторонних агентов. Inworld legacy Studio завершил работу, поэтому рассматривается актуальный runtime SDK.',[5,6,7,10,11,12,13]);
 table(s,[['Решение','Подтверждённая область','Связь с работой'],['Unity AI','Помощник Editor и агенты через Gateway','Authoring, отдельный runtime NPC'],['Convai / Inworld','NPC, знания и действия','Существующие подходы к персонажам'],['Gaia / Procedural Worlds','Создание окружений Unity','Более широкий Terrain authoring'],['LLMUnity','Локальные LLM и RAG в Unity','Близкий локальный runtime'],['LangChain / LlamaIndex','Retrieval и vector store abstractions','Общие архитектурные подходы']],184,400,[278,442,432],22);
 text(s,'WorldWeaver объединяет Terrain, RAG, маршруты моделей и проверяемые действия',64,620,1152,36,24,{bold:true,color:C.teal});
}
{
 const s=slide('Основные требования','Техническое задание','Полные требования FR-01–FR-16 и приёмочные критерии находятся в техническом задании. Основные требования определяют границы архитектуры и критерии приёмки. Голос и PDF-импорт не входят в подтверждённые функции версии.');
 table(s,[['Область','Требование'],['Подключение','UPM-пакет и самостоятельное окно Editor'],['Генерация мира','Текстовое описание, TerrainSpec, seed и Undo'],['Персонажи','Не менее двух NPC, личность и свободный диалог'],['Знания','TXT / MD / JSON, scope, embeddings и память сеанса'],['Действия','Разрешённые цели, лимиты Inventory и NavMesh'],['Модели','Раздельные Terrain / Dialogue / Planning routes']],182,444,[280,872],24);
}
{
 const s=slide('Прецеденты использования','Проектирование','Диаграмма прецедентов определяет роли пользователя. Полная UML-диаграмма: docs/uml/use-case.puml. Разработчик настраивает инструментарий, игрок взаимодействует с NPC, а автономное планирование использует общий исполнитель.');
 text(s,'Разработчик',64,288,185,40,26,{bold:true});text(s,'Игрок',64,532,185,40,26,{bold:true});
 const a=shape(s,'Настройка моделей и NPC',342,192,380,70,{geometry:'ellipse'});
 const b=shape(s,'Генерация Terrain',342,295,380,70,{geometry:'ellipse'});
 const c=shape(s,'Индексация знаний',342,398,380,70,{geometry:'ellipse'});
 const d=shape(s,'Свободный диалог',342,518,380,70,{geometry:'ellipse'});
 const e=shape(s,'Проверка действий',878,398,330,80,{geometry:'ellipse',fill:C.pale});
 const f=shape(s,'Автономное планирование',838,195,370,80,{geometry:'ellipse'});
 [227,330,433].forEach(yy=>line(s,230,310,112,yy-310,C.muted));line(s,196,552,146,0,C.muted);
 connect(s,d,e,'right','left',{dashed:true});connect(s,f,e,'bottom','top',{dashed:true});text(s,'«include»',760,462,130,30,21,{color:C.muted});
 text(s,'Разработчик также исследует PipelineTrace и причины отклонения действий',342,617,850,35,23,{color:C.muted});
}
{
 const s=slide('Обработка реплики и автономной цели','Диаграмма деятельности','Диаграмма деятельности показывает единый путь Dialogue и Planning. Полная версия: docs/uml/activity.puml. Каждый отказ сохраняется в trace и не запускает предметные изменения. Весь пакет встроенных действий проходит предварительную проверку до исполнения.');
 const a=shape(s,'Реплика / цель',64,205,240,62);
 const b=shape(s,'Контекст и RAG',395,205,280,62);
 const c=shape(s,'Выбор ModelTask',766,205,280,62);
 const d=shape(s,'HTTP и JSON',766,340,280,62);
 const e=shape(s,'Валидация\nуспешна?',395,321,280,105,{geometry:'diamond',size:22,fill:C.pale});
 const f=shape(s,'Исполнить действия',64,340,240,62);
 const g=shape(s,'Speech и память',64,518,240,62);
 const h=shape(s,'PipelineTrace',395,518,280,62,{fill:C.pale});
 const i=shape(s,'Контролируемый отказ',766,518,340,62,{stroke:C.orange});
 connect(s,a,b);connect(s,b,c);connect(s,c,d,'bottom','top');connect(s,d,e,'left','right');connect(s,e,f,'left','right');text(s,'да',325,343,55,27,21,{color:C.teal});
 connect(s,e,h,'bottom','top');text(s,'нет',548,459,60,28,21,{color:C.orange});connect(s,f,g,'bottom','top');connect(s,g,h);connect(s,d,i,'bottom','top');connect(s,i,h,'left','right');
 text(s,'Сцена изменяется после проверки всего решения',64,622,1120,35,26,{color:C.teal,bold:true});
}
{
 const s=slide('Диалог с передачей предмета','Диаграмма последовательности','Диаграмма последовательности показывает порядок вызовов при диалоге. Полная версия: docs/uml/sequence.puml. Inventory изменяется только после строгого разбора NpcDecision и проверки dispatcher. Запись памяти отражает реальный результат исполнения.');
 const xs=[115,350,590,830,1080];const labs=['Игрок / NPC','Pipeline','RAG','LLM provider','Dispatcher'];
 xs.forEach((x,i)=>{shape(s,labs[i],x-90,184,180,56,{size:22});line(s,x,240,0,345,C.gray,true);});
 arrow(s,xs[0],284,xs[1],284,'TalkAsync');arrow(s,xs[1],348,xs[2],348,'Retrieve');arrow(s,xs[2],405,xs[1],405,'chunks',{dashed:true});
 arrow(s,xs[1],460,xs[3],460,'messages и JSON Schema');arrow(s,xs[3],515,xs[1],515,'NpcDecision',{dashed:true});
 arrow(s,xs[1],568,xs[4],568,'Parse и Execute');
 text(s,'Проверка лимитов\nи Inventory.Transfer',958,600,250,58,23,{bold:true,color:C.teal});
 text(s,'HTTP-успех отдельно от допустимости игрового действия',64,627,820,35,25,{color:C.muted});
}
{
 const s=slide('Компоненты инструментария','Архитектура','Компоненты соответствуют docs/uml/component.puml. Editor использует runtime-контракты. Сетевой слой не изменяет Inventory или TerrainData непосредственно.');
 const ui=shape(s,'WorldWeaverWindow\nEditor',64,210,280,88,{fill:C.pale});
 const terrain=shape(s,'TerrainAuthoring',64,374,280,70);const generator=shape(s,'TerrainGenerator',64,528,280,70);
 const npc=shape(s,'IntelligentNPC',461,210,280,70);const pipe=shape(s,'NpcPipeline',461,374,280,70,{fill:C.pale});
 const router=shape(s,'ModelRouter',898,210,306,70);const rag=shape(s,'RagIndex',898,374,306,70);const acts=shape(s,'ActionDispatcher',898,528,306,70);
 connect(s,ui,npc);connect(s,ui,terrain,'bottom','top');connect(s,terrain,generator,'bottom','top');connect(s,npc,pipe,'bottom','top');connect(s,pipe,router,'right','left');connect(s,pipe,rag);connect(s,pipe,acts,'right','left');
 text(s,'Providers',922,296,250,34,23,{color:C.muted});text(s,'Embedding + vector store',894,460,320,36,23,{color:C.muted});text(s,'Inventory + NavMeshAgent',872,619,350,34,23,{color:C.muted});
}
{
 const s=slide('Контракты и точки расширения','Диаграмма классов','Контракты и связи соответствуют docs/uml/class.puml. Интерфейсы разделяют модельную маршрутизацию, embeddings, vector store и действие. Пользовательский Validate обязан не менять сцену.');
 const pipe=shape(s,'NpcPipeline\nRunAsync()\nReindexAsync()',64,200,300,140,{fill:C.pale,size:25});
 const route=shape(s,'«interface» IModelRouter\nCompleteAsync(task, request)',467,200,340,100,{size:23});
 const prov=shape(s,'«interface» ILLMProvider\nCompleteAsync() / HealthAsync()',886,200,330,100,{size:22});
 const rag=shape(s,'RagIndex\nRetrieveAsync()\nRememberAsync()',467,400,340,150,{size:24});
 const act=shape(s,'ActionDispatcher\nRegister() / Execute()',64,451,300,100,{size:24});
 const action=shape(s,'«interface» IGameAction\nValidate() / Execute()',64,595,350,66,{size:22});
 const embedding=shape(s,'IEmbeddingProvider',886,400,330,65,{size:24});const vector=shape(s,'IVectorStore',886,507,330,65,{size:24});
 connect(s,pipe,route);connect(s,route,prov);connect(s,pipe,rag,'right','left');connect(s,pipe,act,'bottom','top');connect(s,act,action,'bottom','top');connect(s,rag,embedding);connect(s,rag,vector);
 text(s,'Конкретный backend меняется через интерфейс',467,602,749,55,25,{color:C.teal,bold:true});
}
{
 const s=slide('Потоки данных и граница доверия','DFD','Потоки данных соответствуют docs/uml/dfd.puml. Retrieved-тексты рассматриваются как данные. Модель предлагает JSON, но C# проверяет ограничения. Внешний inference-сервис не получает возможности непосредственно записывать объекты сцены.');
 const user=shape(s,'Игрок / разработчик',64,205,280,66);const context=shape(s,'1. Контекст',395,205,280,66,{geometry:'ellipse'});const rag=shape(s,'2. Retrieval',830,205,340,66,{geometry:'ellipse'});
 const know=shape(s,'Знания и память',64,374,280,78,{geometry:'can'});const trace=shape(s,'PipelineTrace',395,374,280,78,{geometry:'can'});const llm=shape(s,'LLM API',830,374,340,78,{stroke:C.orange});
 const world=shape(s,'Состояние сцены',64,561,280,66,{geometry:'can'});const valid=shape(s,'4. Валидация\nи исполнение',395,539,280,110,{geometry:'ellipse',fill:C.pale});const route=shape(s,'3. Маршрут',830,561,340,66,{geometry:'ellipse'});
 connect(s,user,context);connect(s,context,rag);connect(s,know,rag,'top','left');
 line(s,1170,238,60,0,C.teal);line(s,1230,238,0,412,C.teal);line(s,1230,650,-230,0,C.teal);arrow(s,1000,650,1000,627);
 connect(s,route,llm,'top','bottom',{bidirectional:true});connect(s,route,valid,'left','right');connect(s,valid,world,'left','right');connect(s,valid,trace,'top','bottom');
 text(s,'Ограничение Scope',64,476,280,40,23,{color:C.muted});text(s,'messages\nJSON',840,478,140,62,22,{color:C.orange});text(s,'запрос',690,205,130,28,21,{color:C.muted});text(s,'NpcDecision',686,548,145,28,21,{color:C.muted});
}
{
 const s=slide('Развёртывание и модельные маршруты','Технологический стек','Unity 6000.3.27f1 и C#. Ollama 0.32.6 работает локально на Apple M4 с Metal. EmbeddingGemma использует 768-мерные векторы. В QA зафиксирована Qwen3.5:2b, в финальной демонстрации Dialogue/Planning работает Qwen2.5-Coder 7.6B. Эти результаты не смешиваются.',[1,17,18,19,20,23]);
 text(s,'Apple M4 · macOS 15.7.4 · 16 ГБ объединённой памяти',64,185,1152,40,27,{bold:true,color:C.navy});
 const unity=shape(s,'Unity 6\nWorldWeaver UPM\nC# + Newtonsoft.Json',64,280,350,170,{fill:C.pale,size:28});
 const localnode=shape(s,'Ollama / loopback HTTP\nTerrain и QA: Qwen3.5 2B\nDialogue / Planning: Qwen2.5-Coder 7.6B\nEmbeddings: EmbeddingGemma',532,260,676,205,{size:25});
 const free=shape(s,'Pollinations Classic API / HTTPS\nБесплатный Dialogue: GPT-OSS 20B',532,540,676,90,{stroke:C.orange,size:26});
 connect(s,unity,localnode);connect(s,unity,free,'right','left');
 text(s,'Раздельные маршруты\nTerrain / Dialogue / Planning',64,507,350,85,26,{color:C.teal,bold:true});
}
{
 const s=slide('Генерация Terrain в Unity','Реализованный интерфейс','Скриншоты получены из реального Unity Editor. Слева окно инструментария с TerrainSpec, справа сгенерированная демонстрационная сцена. Проверка диапазонов предшествует генерации, seed задаёт повторяемость, Undo действует на собственный GeneratedTerrain. Источники: docs/assets/screenshots/toolkit-world.png и demo-scene.png.',[3,25]);
 await image(s,'toolkit-world.png',64,183,650,410,{left:0,top:0.18,right:0.52,bottom:0.25});await image(s,'demo-scene.png',752,183,464,410,{left:0.18,top:0.14,right:0.18,bottom:0.12});
 text(s,'Текстовое описание и типизированная спецификация',64,611,650,45,24,{color:C.teal,bold:true});text(s,'Сгенерированный мир',752,611,464,45,24,{color:C.teal,bold:true});
}

{
 const s=slide('Программные испытания','Верификация','Данные непосредственно из XML Unity Test Framework. EditMode проверяет алгоритмы, RAG, строгий JSON, лимиты и HTTP-сбои. PlayMode проверяет передачу предметов, движение, Follow и StopMovement. Реальная интеграция использует настоящие HTTP-запросы и отдельные JSON-протоколы.',[24]);
 table(s,[['Группа','Пройдено','Что подтверждает'],['EditMode',await xmlCounts('editmode-unity6.xml'),'Контракты, RAG, Terrain, Inventory и транспорт'],['PlayMode',await xmlCounts('playmode-unity6.xml'),'Инвентарь, NavMesh и остановку движения'],['Real provider',await xmlCounts('real-integration-unity6.xml'),'Локальный и бесплатный облачный сценарии']],199,287,[260,200,692],26);
 text(s,'Проверки отказов',64,533,400,40,30,{bold:true,color:C.navy});
 text(s,'Неверный JSON, неизвестная цель, двойное расходование,\nотмена, timeout, 429 и отсутствие частичного изменения Inventory',64,588,1152,70,26);
}
{
 const s=slide('Реальный сценарий торговца','Интеграционная проверка','По одному финальному интеграционному прогону каждого маршрута. Сохранены все реплики, raw JSON, решение dispatcher и изменение позиции. Локальная модель передала предмет на втором ходе, а облачная на первом. Планировщик Qwen2.5-Coder выбрал MoveTo(House). Это подтверждение конкретных прогонов, не оценка 100% устойчивости. Источники: results/end-to-end-local.json, results/end-to-end-cloud.json, results/real-integration-unity6.xml.');
 table(s,[['Показатель','Локальный маршрут','Бесплатный cloud'],['Dialogue','Qwen2.5-Coder 7.6B','GPT-OSS 20B'],['Ходов до передачи',String(local.dialogue_turns.length),String(cloud.dialogue_turns.length)],['Crystal: NPC / Player','10 / 0 → 9 / 1','10 / 0 → 9 / 1'],['Planning','MoveTo(House), локально','MoveTo(House), локально'],['Перемещение за 3 с',ru(endValue(local,'moved_distance','distance_moved','distance'),2)+' м',ru(endValue(cloud,'moved_distance','distance_moved','distance'),2)+' м']],186,386,[352,400,400],24);
 text(s,'Отдельная политика Planning исключает повторную передачу предметов',64,605,1152,38,25,{color:C.teal,bold:true});
}
{
 const q=summary.qa;const s=slide('Совпадение с известным ответом','Эксперимент: 30 вопросов × 3 конфигурации','Столбцы содержат количества из results/summary.json. Raw: совпадение известного факта в сгенерированной speech. Delivered: совпадение в реплике после принятия всего решения dispatcher. n=30 на конфигурацию. Полный протокол сохранён по тегу experiment-2026-10-30 до финальных исправлений Planning/schema. Метрика проверяет нормализованные варианты, а не полную groundedness.');
 chart(s,{position:{left:64,top:188,width:850,height:405},categories:['Qwen 2B без RAG','Qwen 2B с RAG','Cloud 20B с RAG'],series:[{name:'Сгенерированная речь',values:[q['local-no-rag'].fact_match_count,q['local-rag'].fact_match_count,q['cloud-rag'].fact_match_count],fill:C.teal},{name:'Доставленная речь',values:[q['local-no-rag'].delivered_fact_match_count,q['local-rag'].delivered_fact_match_count,q['cloud-rag'].delivered_fact_match_count],fill:C.navy}],barOptions:{direction:'column',grouping:'clustered',gapWidth:85},hasLegend:true,legend:{position:'bottom',textStyle:{fontSize:22}},yAxis:{min:0,max:30,majorUnit:10,numberFormatCode:'0',textStyle:{fontSize:20,fill:C.muted},majorGridlines:{fill:C.gray,width:1}},xAxis:{textStyle:{fontSize:21,fill:C.ink}},dataLabels:{showValue:true,position:'outEnd',numberFormatCode:'0',textStyle:{fontSize:23,bold:true,fill:C.ink}},chartFill:C.bg,plotAreaFill:C.bg});
 text(s,'Retrieval hit@3',952,210,260,40,27,{bold:true,color:C.navy});text(s,'30 / 30',952,265,260,66,48,{bold:true,color:C.teal});text(s,'у обеих RAG-конфигураций',952,341,260,80,24);
 text(s,'Разбор JSON',952,458,260,40,27,{bold:true,color:C.navy});text(s,'30 / 30 · локально\n29 / 30 · cloud',952,511,260,85,26);
 text(s,'Часть ответов не дошла до игрока после проверки предложенных действий',64,625,1152,38,24,{color:C.muted});
}
{
 const q=summary.qa;const s=slide('Задержки и генерация Terrain','Измеренные характеристики','Время QA включает retrieval, очередь бесплатного API и запрос. p50 cloud provider отдельно 10,2 с. В API установлен интервал 31 с. Terrain: 10 seed на каждое разрешение, только алгоритм C# без сети и импорта ассетов. 40 из 40 повторов дали идентичные массивы в данной среде. Источник: results/summary.json и сырые results/*.jsonl.');
 chart(s,{position:{left:64,top:238,width:691,height:342},categories:['Локально без RAG','Локально с RAG','Cloud с RAG'],series:[{name:'Полное время p50, с',values:[q['local-no-rag'].p50_ms/1000,q['local-rag'].p50_ms/1000,q['cloud-rag'].p50_ms/1000],fill:C.teal}],hasLegend:false,barOptions:{direction:'bar',grouping:'clustered',gapWidth:100},xAxis:{textStyle:{fontSize:21,fill:C.ink}},yAxis:{min:0,max:35,majorUnit:10,numberFormatCode:'0" с"',textStyle:{fontSize:20,fill:C.muted},majorGridlines:{fill:C.gray,width:1}},dataLabels:{showValue:true,position:'outEnd',numberFormatCode:'0.0" с"',textStyle:{fontSize:23,bold:true,fill:C.ink}},chartFill:C.bg,plotAreaFill:C.bg});
 text(s,'Полное время QA · p50, с',64,190,691,38,27,{bold:true,color:C.navy});
 text(s,'Terrain · медиана C#',815,190,400,38,27,{bold:true,color:C.navy});
 table(s,[['Сетка','Время, мс'],...summary.terrain.map(t=>[`${t.resolution} × ${t.resolution}`,ru(t.median_ms,2)])],247,300,[190,210],23,815,400);
 text(s,'40 / 40 повторов совпали',815,575,400,45,26,{bold:true,color:C.teal});
 text(s,'Cloud включает очередь бесплатного API. Terrain: только алгоритм C#, без сети',64,630,1152,32,23,{color:C.muted});
}
{
 const s=slide('Индивидуальные знания и реальный диалог','Демонстрационная сцена','Реальные изображения Unity. KnowledgeBase разделяет документы по персонажам. Play Mode показывает фактический разговор и остатки Inventory. Скриншот не служит источником статистической оценки: численные результаты находятся в JSON/XML. Источники: docs/assets/screenshots/toolkit-knowledge.png, play-dialogue.png.');
 await image(s,'toolkit-knowledge.png',64,184,365,430);await image(s,'play-dialogue.png',466,184,750,430,{left:0.16,top:0.14,right:0.39,bottom:0.2});
 text(s,'KnowledgeBase и Scope',64,628,365,32,23,{color:C.teal,bold:true});text(s,'Речь модели и состояние Inventory',466,628,750,32,23,{color:C.teal,bold:true});
}
{
 const s=slide('Результат и границы применения','Заключение','Разработан переносимый UPM-инструментарий с Editor, TerrainSpec, индивидуальным RAG, модельными маршрутами и проверкой действий. Вклад инженерный и экспериментальный. Ограничения подтверждены в пояснительной записке. Нулевая плата за запросы не означает нулевые затраты оборудования и разработки. Дополнительный GUI-прогон локального Planning выбрал пустой actions и перемещение 0 м. Допустимое бездействие не считается выполнением демонстрационного критерия движения. Один успешный интеграционный прогон не доказывает устойчивости.');
 text(s,'WorldWeaver объединяет генерацию рельефа,\nзнания персонажей и проверяемые действия в Unity',64,195,1152,95,36,{bold:true,color:C.navy});
 text(s,'Подтверждённые результаты',64,334,500,40,29,{bold:true,color:C.teal});
 text(s,`UPM-пакет и демонстрационная сцена\n${await xmlCounts('editmode-unity6.xml')} EditMode, ${await xmlCounts('playmode-unity6.xml')} PlayMode, ${await xmlCounts('real-integration-unity6.xml')} реальные интеграции\n90 запросов и 40 повторов Terrain`,64,393,1152,117,28);
 text(s,'Границы версии',64,544,500,40,29,{bold:true,color:C.teal});
 text(s,'Малый корпус, память сеанса, ограниченный TerrainSpec.\nЛокальный Planning может бездействовать. Cloud зависит от доступности API.',64,599,1152,65,26);
}

// Measured movement is mandatory; missing source fields must fail the build.
for (const item of manifest) if(item.slide===16) {
  // Exact movement field names are validated before any presentation export.
  const lk=Object.keys(local).find(k=>/distance/i.test(k));const ck=Object.keys(cloud).find(k=>/distance/i.test(k));
  if(!lk||!ck)throw new Error('Missing measured e2e movement');
}
const candidate=path.join(out,'candidate.pptx');
await (await PresentationFile.exportPptx(pres)).save(candidate);
for(let i=0;i<pres.slides.items.length;i++){
 const s=pres.slides.items[i];const stem=`slide-${String(i+1).padStart(2,'0')}`;
 const png=await pres.export({slide:s,format:'png',scale:1});await fs.writeFile(path.join(out,stem+'.png'),new Uint8Array(await png.arrayBuffer()));
}
await fs.writeFile(path.join(out,'manifest.json'),JSON.stringify(manifest,null,2));
const montage=await pres.export({format:'webp',montage:true});await fs.writeFile(path.join(out,'montage.webp'),new Uint8Array(await montage.arrayBuffer()));
if(process.argv.includes('--final')) {
 const finalPath=path.join(root,'deliverables',process.env.WORLDWEAVER_PPTX_NAME??'Презентация_WorldWeaver_финал.pptx');
 const result=await finalizePresentation({workspaceDir:root,candidatePath:candidate,finalPath,pythonExecutable:path.join(runtime,'python/bin/python3'),integrityValidatorPath:path.join(skill,'container_tools/inspect_presentation_package_integrity.py'),layoutValidatorPath:path.join(skill,'container_tools/inspect_presentation_layout_geometry.py'),layoutArgs:['--expected-slide-size-emu','12192000,6858000','--validate-bullet-geometry','--validate-heading-fit','--require-native-table-slide','5','--require-native-table-slide','15','--require-native-table-slide','16','--require-native-table-slide','18'],explicitTotalSlideCount:20,requiredNativeTableOwnerSlides:[5,15,16,18],requiredNativeChartOwnerSlides:[17,18],materializeLiteralChartWorkbooks:true,fontPolicy:{basis:'design',families:[FONT]},verifyArtifactToolImport:true,receiptPath:path.join(out,process.env.WORLDWEAVER_PPTX_RECEIPT??'validation-requests.json')});
 console.log(JSON.stringify(result));
}
console.log(`Rendered ${pres.slides.items.length} slides in ${out}`);
