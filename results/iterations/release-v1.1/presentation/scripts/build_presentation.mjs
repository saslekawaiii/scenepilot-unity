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
const skill = process.env.WW_PRESENTATIONS_SKILL ?? path.join(os.homedir(), '.codex/plugins/cache/openai-primary-runtime/presentations/26.1026.10807/skills/presentations');
const out = path.join(root, '.private/presentation');
await fs.mkdir(out, { recursive: true });
await fs.mkdir(path.join(root, 'deliverables'), { recursive: true });
const summary = JSON.parse(await fs.readFile(path.join(root, 'results/summary.json'), 'utf8'));
const sources = JSON.parse(await fs.readFile(path.join(root, 'docs/research/sources.json'), 'utf8'));
// This deck separates controller tests, decoder probes and actual model acceptance.
// Refuse to load the presentation runtime while the final model run is still writing.
const readJson = async name => JSON.parse(await fs.readFile(path.resolve(root, name), 'utf8'));
const schema = await readJson('results/editor-schema-verification.json');
const decoderSmoke = await readJson('results/editor-schema-compatibility-smoke.json');
const codeRun = await readJson('results/chat-code-acceptance.json');
const journalRun = await readJson('results/chat-code-journal-acceptance.json');
const testRuns = Object.fromEntries(await Promise.all(['chat-agent-editmode.xml','chat-agent-playmode.xml'].map(async name=>[name,await xmlDetails(name)])));
const editStats = testRuns['chat-agent-editmode.xml'];
const acceptancePath = process.env.WW_CHAT_ACCEPTANCE_RESULT ?? 'results/chat-agent-acceptance-cloud-v4.json';
const localAcceptancePath = process.env.WW_CHAT_LOCAL_ACCEPTANCE_RESULT ?? 'results/chat-agent-acceptance-local9b-v2.json';
const acceptance = await readJson(acceptancePath);
const localAcceptance = await readJson(localAcceptancePath);
if (!acceptance.finishedUtc || !localAcceptance.finishedUtc) throw new Error('A final chat acceptance is still running. Wait for both finishedUtc fields before building this deck.');
const actualCases = acceptance.tests ?? [];
const actualPassed = actualCases.filter(item => item.passed === true).length;
const actualCompleted = actualCases.length;
const localCases = localAcceptance.tests ?? [];
const localPassed = localCases.filter(item => item.passed === true).length;
const localCompleted = localCases.length;
const isCloudV2 = path.basename(acceptancePath) === 'chat-agent-acceptance-cloud-v2.json';
const cloudTerrainLexicalFailure = path.basename(acceptancePath) === 'chat-agent-acceptance-cloud-v4.json' && actualCases.some(item => item.number===7 && item.reasons?.includes('The explanation did not describe the actual terrain.') && item.response?.toLowerCase().includes('террейн'));
const actualPlanned = 7;
const caseNames = ['Мир и точное количество объектов', 'Изменение существующего Terrain', 'NPC с индивидуальным знанием', 'Изменение цели персонажа', 'Три новых источника света', 'Черновик C# для просмотра', 'Модель и объяснение состояния сцены'];
const { Presentation, PresentationFile } = await import(pathToFileURL(path.join(runtime, 'node/node_modules/@oai/artifact-tool/dist/artifact_tool.mjs')).href);
const { applyPresentationChartFont, finalizePresentation } = await import(pathToFileURL(path.join(skill, 'container_tools/artifact_tool_utils.mjs')).href);
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
  text(s,'WorldWeaver 1.1 · ВолгГТУ',64,678,800,21,16,{color:C.muted});
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
async function xmlDetails(name){const x=await fs.readFile(path.join(root,'results',name),'utf8');const m=x.match(/<test-run\b([^>]+)>/);if(!m)throw new Error('Missing NUnit test-run: '+name);const a={};for(const t of m[1].matchAll(/([\w-]+)="([^"]*)"/g))a[t[1]]=t[2];const groups={};for(const t of x.matchAll(/<test-case\b[^>]*\bclassname="([^"]+)"/g)){const key=t[1].split('.').at(-1);groups[key]=(groups[key]??0)+1;}return {...a,groups};}
async function xmlCounts(name){const a=testRuns[name];if(!a)throw new Error('Unloaded NUnit test run: '+name);return `${a.passed}/${a.total}`;}
function evidenceNotes(...names){return '\nИсточники проекта:\n'+names.map(name=>path.resolve(root,name)).join('\n');}
function checkGroup(keys){return `${keys.filter(key=>codeRun.checks?.[key]===true).length}/${keys.length}`;}

// 01. The approved thesis subject remains unchanged.
{
 const s=pres.slides.add();s.background.fill=C.navy;
 text(s,'ВОЛГОГРАДСКИЙ ГОСУДАРСТВЕННЫЙ\nТЕХНИЧЕСКИЙ УНИВЕРСИТЕТ',64,42,1120,60,22,{color:C.white});
 text(s,'Разработка интеллектуального\nинструментария для автоматизированного\nсоздания динамических игровых миров',64,194,1152,250,48,{color:C.white,bold:true,name:'slide-title'});
 text(s,'Выпускная квалификационная работа',64,473,1080,36,26,{color:'#B9DCDD'});
 text(s,'Коваленко Кирилл Дмитриевич\nИВТ-465 · 09.03.01 Информатика и вычислительная техника',64,550,1140,76,25,{color:C.white});
 text(s,'ФЭВТ · кафедра САПР и ПК · Волгоград, 2027',64,664,1120,25,19,{color:'#B9DCDD'});
 s.speakerNotes.text='Исходная тема от 30.09.2026 сохранена. Профиль «Разработка игровых и XR приложений». Имя руководителя не подставлено: в найденных материалах оно не установлено. Год 2027 соответствует найденному плану защиты. Демонстрируется версия WorldWeaver 1.1 с чат-интерфейсом.\nИсточники: локальное ТЗ и учебные документы автора.';
 manifest.push({slide:1,title:'Титульный слайд',section:'Введение'});
}
// 02
{
 const s=slide('Создание и изменение игрового мира','Актуальность','Задача работы состоит в интеграции LLM, процедурной генерации и знаний NPC в Unity. В версии 1.1 разработчик описывает задачу в чате, а результат проверяется по фактическому состоянию сцены. Не заявляется первенство чата, локальных моделей или RAG.',[14,15,5,11]);
 text(s,'Текстовая просьба должна приводить\nк проверяемому результату в Unity',64,195,1120,94,36,{bold:true,color:C.navy});
 rows(s,['Мир сочетает Terrain, объекты, свет и персонажей.','Продолжение диалога должно изменять уже созданные объекты.','Результат модели требует проверки структуры и правил исполнения.'],64,334,1152,94,28);
}
// 03
{
 const s=slide('Цель и задачи работы','Постановка задачи','Цель: разработать переносимый инструментарий Unity для автоматизированного создания и изменения игровых миров. Версия 1.1 добавляет диалоговое authoring, 18 именованных инструментов и просмотр генерируемого C# перед импортом. Runtime-модуль сохраняет TerrainSpec, индивидуальный RAG и проверяемые действия NPC.'+evidenceNotes('docs/adr/ADR-005-chat-agent.md'));
 text(s,'Разработать инструментарий Unity\nс диалоговым созданием мира и контекстными NPC',64,195,1152,95,34,{bold:true,color:C.navy});
 rows(s,['Спроектировать контракт запроса, модели, инструментов и истории.','Реализовать создание сцены, индивидуальные знания NPC и цикл C#.','Проверить код, совместимость декодера и реальные запросы модели.'],64,340,1152,93,27);
}
// 04
{
 const s=slide('План решения','Организация работы','Этапы охватывают исходное исследование NPC/RAG и разработку чат-агента 1.1. Программные тесты, прямые пробы декодирования и последовательная беседа с моделью имеют разные критерии.'+evidenceNotes('docs/research/chat-agent-test-summary.md'));
 table(s,[['Этап','Проверяемый результат'],['Анализ','Аналоги и границы автоматизации Unity'],['Проектирование','Архитектура, UML и типизированные контракты'],['Реализация','UPM-пакет, чат, сцена, NPC и просмотр C#'],['Проверка','Unity tests, компиляция и реальная беседа'],['Исследование','Архивный эксперимент RAG версии 1.0']],190,418,[270,882],25);
}
// 05
{
 const s=slide('Сопоставление с аналогами','Анализ решений','Сравнение основано на первичных источниках и функциональных границах. Одинаковый сравнительный бенчмарк коммерческих продуктов не проводился. Unity AI поддерживает authoring и агентов через Gateway. Inworld legacy Studio закрыт, поэтому рассматривается runtime SDK.'+evidenceNotes('docs/research/chat-agent-comparison.md'),[5,6,7,10,11,12,13]);
 table(s,[['Решение','Подтверждённая область','Связь с работой'],['Unity AI','Помощник Editor и агенты через Gateway','Диалоговое authoring'],['Convai / Inworld','NPC, знания и действия','Runtime-персонажи'],['Gaia / Procedural Worlds','Создание окружения Unity','Terrain authoring'],['LLMUnity','Локальные LLM и RAG в Unity','Локальный runtime'],['LangChain / LlamaIndex','Retrieval и vector stores','Архитектура знаний']],184,400,[278,442,432],22);
 text(s,'Область WorldWeaver: чат, инструменты сцены, Terrain и знания NPC',64,620,1152,36,24,{bold:true,color:C.teal});
}
// 06
{
 const s=slide('Требования и 18 инструментов','Техническое задание','Состав инструментов: create_world, update_world, scatter_objects; create_object, set_transform, set_material, delete_object, attach_behavior; create_npc, update_npc; set_lighting, create_camera; inspect_object, search_project, read_script, save_scene; draft_script, attach_script. Схема содержит 19 ветвей для 18 уникальных инструментов: set_lighting разделяет создание и изменение. Перечень ограничен реализованным SceneToolExecutor и ScriptDrafts.'+evidenceNotes('results/editor-schema-verification.json','docs/adr/ADR-005-chat-agent.md'));
 table(s,[['Область','Число','Возможности'],['Мир и окружение','3','Создать, изменить и разместить декорации'],['Объекты и поведение','5','Геометрия, Transform, Material, удаление и пресеты'],['Персонажи','2','Создать NPC и изменить его данные'],['Свет и камера','2','Параметры Light и новая камера'],['Чтение и сохранение','4','Inspect, поиск, чтение C# и явный save_scene'],['Код C#','2','Draft с просмотром и подключение после компиляции']],182,415,[275,115,762],22);
 text(s,'Неизвестные инструменты и аргументы отклоняются до исполнения',64,620,1152,36,25,{bold:true,color:C.teal});
}
// 07. Editable use-case diagram.
{
 const s=slide('Прецеденты использования','Проектирование','Редактируемая диаграмма показывает роли версии 1.1. Разработчик использует чат, проверяет код и управляет историей. Игрок взаимодействует с созданным NPC через runtime-пайплайн. Просмотр C# требуется перед импортом независимо от AutoApply. Внизу реальный фрагмент GUI-ответа Игната о времени открытия ворот: Qwen3.5 9B с EmbeddingGemma, 8192 context / 1024 max_tokens, одна GUI-проверка, 51902 мс. Это отдельное наблюдение runtime-ответа, не оценка надёжности или облачной модели. Фрагмент скриншота не изменён.'+evidenceNotes('Packages/com.worldweaver.toolkit/Editor/Assistant/WorldWeaverChatWindow.cs','docs/adr/ADR-005-chat-agent.md','docs/assets/screenshots/chat-npc-play-reply.png'));
 text(s,'Разработчик',64,268,190,40,26,{bold:true});text(s,'Игрок',64,469,190,40,26,{bold:true});
 const a=shape(s,'Создать и изменить мир',342,180,400,64,{geometry:'ellipse'});
 const b=shape(s,'Настроить персонажа и знания',342,270,400,64,{geometry:'ellipse'});
 const c=shape(s,'Просмотреть и импортировать C#',342,360,400,64,{geometry:'ellipse'});
 const d=shape(s,'Диалог с персонажем',342,454,400,70,{geometry:'ellipse'});
 const e=shape(s,'Проверить и исполнить план',876,267,332,88,{geometry:'ellipse',fill:C.pale});
 const f=shape(s,'Проверить игровое действие',876,446,332,88,{geometry:'ellipse',fill:C.pale});
 [212,302,392].forEach(yy=>line(s,230,288,112,yy-288,C.muted));line(s,196,489,146,0,C.muted);
 connect(s,a,e,'right','top',{dashed:true});connect(s,b,e,'right','left',{dashed:true});connect(s,c,e,'right','bottom',{dashed:true});connect(s,d,f,'right','left',{dashed:true});
 text(s,'NPC: локальная 9B\nОдна GUI-проверка',64,581,245,58,22,{color:C.muted});
 await image(s,'chat-npc-play-reply.png',342,566,866,96,{left:.518,top:.409,right:.206,bottom:.529});
}
// 08. Editable activity diagram.
{
 const s=slide('Цикл редакторского агента','Диаграмма деятельности','Контроллер получает задачу, собирает ограниченный снимок сцены, запрашивает EditorPlan и проверяет все инструменты до изменений. Ошибочный ответ возвращается модели для исправления. После исполнения контроллер передаёт фактический read-back и исходную цель, включая случай complete:true. Code tools переходят в PendingPlan и требуют просмотра. Число раундов ограничено выбранным режимом.'+evidenceNotes('Packages/com.worldweaver.toolkit/Editor/Assistant/ChatController.cs'));
 const a=shape(s,'Запрос разработчика',64,196,280,68);
 const b=shape(s,'История и снимок сцены',485,196,300,68);
 const c=shape(s,'Модель и EditorPlan',912,196,304,68);
 const d=shape(s,'Контракт допустим?',912,337,304,94,{geometry:'diamond',size:22,fill:C.pale});
 const e=shape(s,'Сцена или просмотр C#',485,350,300,68);
 const f=shape(s,'Фактический результат',64,350,280,68);
 const g=shape(s,'Проверка цели моделью',64,532,280,68,{fill:C.pale});
 const h=shape(s,'История и завершение',485,532,300,68);
 const i=shape(s,'Ошибка для исправления',912,532,304,68,{stroke:C.orange});
 connect(s,a,b);connect(s,b,c);connect(s,c,d,'bottom','top');connect(s,d,e,'left','right');text(s,'да',822,359,60,30,21,{color:C.teal});
 connect(s,e,f,'left','right');connect(s,f,g,'bottom','top');connect(s,g,h);connect(s,d,i,'bottom','top');text(s,'нет',1076,468,60,28,21,{color:C.orange});
 line(s,1216,566,28,0,C.orange);line(s,1244,230,0,336,C.orange);arrow(s,1244,230,1216,230,'',{color:C.orange});
 text(s,'Режим ограничивает шаги. Импорт C# требует явного применения.',64,630,1152,35,25,{color:C.teal,bold:true});
}
// 09. Editable sequence diagram.
{
 const s=slide('Создание и проверка объекта','Диаграмма последовательности','Последовательность отражает многошаговый ChatController. Название, instance ID и фактическое положение созданного объекта попадают в следующий запрос. Проверка цели выполняется после изменения даже если модель преждевременно вернула complete:true. Программная регрессия CompleteFlagStillRequiresFreshReadbackAndGoalRepair проверяет этот механизм управляемым провайдером.'+evidenceNotes('Packages/com.worldweaver.toolkit/Tests/EditMode/ChatControllerTests.cs'));
 const xs=[140,385,635,895,1135];const labs=['Разработчик','ChatController','Модель','Executor','Unity'];
 xs.forEach((x,i)=>{shape(s,labs[i],x-94,184,188,56,{size:21});line(s,x,240,0,368,C.gray,true);});
 arrow(s,xs[0],279,xs[1],279,'Запрос');arrow(s,xs[1],337,xs[2],337,'Контекст + schema');arrow(s,xs[2],395,xs[1],395,'EditorPlan',{dashed:true});
 arrow(s,xs[1],453,xs[3],453,'Validate и Execute');arrow(s,xs[3],511,xs[4],511,'Объект');arrow(s,xs[4],562,xs[1],562,'Фактический read-back',{dashed:true});
 text(s,'Следующий запрос модели содержит исходную цель и результат Unity',64,630,1152,35,25,{color:C.teal,bold:true});
}
// 10. Editable component diagram.
{
 const s=slide('Компоненты WorldWeaver 1.1','Архитектура','UI отвечает за представление и вызывает сервис. ChatController управляет историей, моделью, отменой и циклом проверки. EditorToolSchema задаёт типизированные аргументы. SceneToolExecutor проверяет и изменяет сцену. ScriptDrafts выполняет рассмотренный импорт и принимает настоящие compiler events. Runtime NPC сохраняет отдельный NpcPipeline/RAG/ActionDispatcher.'+evidenceNotes('docs/adr/ADR-005-chat-agent.md'));
 const ui=shape(s,'ChatWindow\nUI Toolkit',64,203,298,88,{fill:C.pale,size:25});
 const ctrl=shape(s,'ChatController',491,203,298,88,{fill:C.pale,size:25});
 const llm=shape(s,'ILLMProvider\nOllama / Classic API',918,203,298,88,{size:24});
 const schemaNode=shape(s,'EditorToolSchema',64,382,298,74,{size:24});
 const executor=shape(s,'SceneToolExecutor\nWorldComposer',491,382,298,92,{size:24});
 const scripts=shape(s,'ScriptDrafts\nUnity compiler',918,382,298,92,{size:24});
 const history=shape(s,'ChatHistoryStore\nUserSettings',64,551,298,82,{size:24});
 const runtimeNode=shape(s,'Terrain и runtime NPC\nRAG / ActionDispatcher',491,551,725,82,{size:25});
 connect(s,ui,ctrl);connect(s,ctrl,llm);connect(s,schemaNode,ctrl,'top','left');connect(s,ctrl,executor,'bottom','top');connect(s,ctrl,scripts,'right','top');connect(s,ctrl,history,'left','right');connect(s,executor,runtimeNode,'bottom','top');
}
// 11. Editable class diagram.
{
 const s=slide('Контракты и точки расширения','Диаграмма классов','Диаграмма показывает реальные публичные контракты. ChatController использует EditorPlan и ILLMProvider, сохраняет ChatSession. EditorCommand содержит tool/name/target/args; EditorCommandResult хранит наблюдённый результат. Исполнитель и модуль C# имеют разные границы транзакции.'+evidenceNotes('Packages/com.worldweaver.toolkit/Editor/Assistant/AssistantContracts.cs','Packages/com.worldweaver.toolkit/Editor/Assistant/ChatController.cs'));
 const ctrl=shape(s,'ChatController\nSendAsync()\nApplyPendingAsync()\nRevertCode()',64,196,315,170,{fill:C.pale,size:25});
 const plan=shape(s,'EditorPlan\nreply / complete / actions',474,196,325,94,{size:24});
 const command=shape(s,'EditorCommand\ntool / name / target / args',891,196,325,94,{size:23});
 const session=shape(s,'ChatSession\nMessages / Settings',64,466,315,94,{size:24});
 const provider=shape(s,'«interface» ILLMProvider\nCompleteAsync()',474,384,325,94,{size:23});
 const result=shape(s,'EditorCommandResult\nSuccess / Target / Summary',891,384,325,94,{size:23});
 const execute=shape(s,'SceneToolExecutor\nValidate() / Execute()',474,551,325,94,{size:24});
 const code=shape(s,'ScriptDrafts\nApply() / Revert()',891,551,325,94,{size:24});
 connect(s,ctrl,plan);connect(s,plan,command);connect(s,ctrl,session,'bottom','top');connect(s,ctrl,provider,'right','left');connect(s,execute,result,'right','bottom');connect(s,code,result,'top','bottom');
}
// 12. Editable data-flow diagram.
{
 const s=slide('Потоки данных и проверка изменений','DFD','Контекст сцены и история являются данными. Модель возвращает EditorPlan, не получает доступ к shell или Unity API. Полная runtime-валидация сохраняется при упрощении больших строковых maxLength только для Ollama-декодера. Пользователь рассматривает C# перед импортом. Это review кода, а не sandbox его дальнейшего выполнения.'+evidenceNotes('docs/adr/ADR-005-chat-agent.md','results/editor-schema-verification.json'));
 const user=shape(s,'Разработчик',64,203,290,72);
 const context=shape(s,'1. Снимок и история',491,203,298,72,{geometry:'ellipse'});
 const model=shape(s,'2. Модель',918,203,298,72,{geometry:'ellipse',stroke:C.orange});
 const history=shape(s,'UserSettings\nИстория и результаты',64,390,290,92,{geometry:'can',size:24});
 const validate=shape(s,'3. Проверка плана',491,390,298,92,{geometry:'ellipse',fill:C.pale});
 const proposed=shape(s,'EditorPlan\nТипизированные args',918,390,298,92,{size:24});
 const scene=shape(s,'Сцена и ассеты',64,568,290,72,{geometry:'can'});
 const execute=shape(s,'4. Executor / code review',491,568,298,72,{geometry:'ellipse',size:22});
 connect(s,user,context);connect(s,context,model);connect(s,model,proposed,'bottom','top');connect(s,proposed,validate,'left','right');connect(s,validate,execute,'bottom','top');connect(s,execute,scene,'left','right');connect(s,execute,history,'left','bottom');
 text(s,'Read-back\nи compiler feedback',918,566,298,78,27,{bold:true,color:C.teal});
}
// 13. Editable deployment diagram.
{
 const s=slide('Развёртывание и модели','Технологический стек','Unity 6000.3.27f1, C#, UI Toolkit, Newtonsoft.Json, Unity Test Framework. Минимальная заявленная версия пакета 1.1 — Unity 6000.3. Ollama 0.32.6 работает на Apple M4/Metal. Qwen3.5:9b установлена, что подтверждается отдельным native replay и протоколом её беседы; сам факт установки не оценивает скорость или успешность. Qwen2.5-Coder latest имеет 7.6B, малая Qwen3.5 — около 2B. Бесплатный Pollinations Classic openai-fast отвечает как GPT-OSS20B. Серверный бесплатный интервал 45 секунд, клиентский 46 секунд; max_tokens не более 1500 независимо от мощности. Новые NPC получают маршрут выбранной модели, существующие сохраняют назначение. Архивный QA использует отдельную зафиксированную конфигурацию.'+evidenceNotes('results/chat-agent-environment.json','results/native-argument-order-replay.json',localAcceptancePath,'Packages/com.worldweaver.toolkit/package.json','docs/adr/ADR-005-chat-agent.md'),[1,17,18,19,20,23,39]);
 text(s,'Apple M4, macOS 15.7.4, 16 ГБ объединённой памяти',64,185,1152,40,27,{bold:true,color:C.navy});
 const unity=shape(s,'Unity Editor 6\nWorldWeaver 1.1\nUI Toolkit + C#',64,281,350,170,{fill:C.pale,size:28});
 const localNode=shape(s,'Ollama / loopback HTTP\nQwen3.5 9B\nQwen2.5-Coder 7.6B · Qwen3.5 2B\nEmbeddings: EmbeddingGemma',532,260,676,205,{size:26});
 const free=shape(s,'Pollinations Classic / HTTPS\nGPT-OSS20B · 46 с · max_tokens 1500',532,540,676,90,{stroke:C.orange,size:25});
 connect(s,unity,localNode);connect(s,unity,free,'right','left');
 text(s,'Быстро / Баланс / Глубоко\nзадают бюджет шагов',64,509,350,82,26,{color:C.teal,bold:true});
}
// 14. Actual GUI, re-read from the same paths after root replaces polished screenshots.
{
 const s=slide('Чат и созданный игровой мир','Реализованный интерфейс','Слева скриншот настоящего Unity Editor: история, каталог моделей, режим, четыре стартовых запроса и многострочное поле сообщения. Enter отправляет сообщение, Shift+Enter оставляет новую строку, Escape отменяет текущий запрос. Отдельный просмотр C# сохраняется при автоматическом применении обычных изменений сцены. Справа реальный GameView сохранённой сцены local9b-v2, а не облачного прогона. Из полного кадра Unity выделена область GameView без изменения содержимого.'+evidenceNotes('docs/assets/screenshots/chat-welcome.png','docs/assets/screenshots/chat-scene-result.png',localAcceptancePath));
 await image(s,'chat-welcome.png',64,180,510,450);
 await image(s,'chat-scene-result.png',606,180,610,450,{left:.165,top:.127,right:.181,bottom:.127});
 text(s,'Чат на UI Toolkit',64,633,510,30,24,{color:C.teal,bold:true});
 text(s,'Локальная беседа 9B: фактическая сцена',606,633,610,30,24,{color:C.teal,bold:true});
}
// 15. Native evidence table. These are software tests, not model accuracy.
{
 const s=slide('Программные испытания','Верификация',`EditMode содержит ${editStats.total} случаев: Core ${editStats.groups.CoreTests??0}, Transport ${editStats.groups.TransportTests??0}, SceneTool ${editStats.groups.SceneToolTests??0}, ChatController ${editStats.groups.ChatControllerTests??0}. Это исходные 56 плюс ${Number(editStats.total)-56} дополнительных случаев. Контроллер использует подставленный провайдер, сцена и исполнитель настоящие. Три PlayMode-теста проверяют Inventory, NavMesh и уменьшение массива patrol. Отдельные 11 проверок одного LLM-скрипта и 34 проверки двух синтетических импортов не складываются с XML или друг с другом в долю надёжности. Эти числа не оценивают вероятность выполнения запроса настоящей моделью.`+evidenceNotes('results/chat-agent-editmode.xml','results/chat-agent-playmode.xml','results/chat-code-acceptance.json','results/chat-code-journal-acceptance.json','docs/research/chat-agent-test-summary.md'),[24]);
 table(s,[['Группа','Пройдено','Что подтверждает'],['EditMode',await xmlCounts('chat-agent-editmode.xml'),'Контракты, контроллер, RAG, Terrain и транспорт'],['PlayMode',await xmlCounts('chat-agent-playmode.xml'),'Inventory, NavMesh и runtime-регрессию patrol'],['Один LLM-скрипт',`${codeRun.passedChecks}/${codeRun.totalChecks}`,'Просмотренный исходник: compile, attach, revert'],['Журнал C#',`${journalRun.passedChecks}/${journalRun.totalChecks}`,'Два синтетических импорта, четыре компиляции']],187,336,[260,200,692],23);
 text(s,'Проверки отказов и сохранения результата',64,550,1152,40,28,{bold:true,color:C.navy});
 text(s,'Отмена, rollback, ошибочный complete:true, фактический read-back,\nсохранность KnownEntities и отказ attach до компиляции',64,604,1152,58,24);
}
// 16. Native table for a real, hash-reviewed C# lifecycle, not 11 code-generation tasks.
{
 const s=slide('Реальный цикл проверенного C#','Интеграционная проверка','Один просмотренный YAxisRotator из сохранённой облачной беседы прошёл 11 проверок. Runner сверил SHA-256 ровно строки args.code, применил PendingPlan стандартным контроллером, дождался настоящих событий компиляции, подключил компонент к своему кубу, вызвал стандартный ScriptDrafts.Revert, подтвердил вторую компиляцию и очистку. Файла до опыта не существовало. Новых LLM-запросов в этом runner нет; результат не оценивает качество произвольного C#. Независимый journal-протокол содержит 34 проверки двух вручную подготовленных редакций одного класса: четыре реальные компиляции, отказ attach при старом GetClass до подтверждения B, отказ отката A поверх B и последовательное восстановление B→A. Эти 34 проверки являются испытанием кода журнала, а не результатами генерации ещё двух программ.'+evidenceNotes('results/chat-code-acceptance.json','results/chat-code-source-review.json','results/chat-code-journal-acceptance.json','results/chat-code-journal-source-review.json'));
 const groups=[['Импорт',['importedFileReadback'],'Исходник совпал с просмотренным кодом'],['Компиляция',['compilerHistorySuccess','compilerTypeValid','importCompilationFinished'],'История, настоящий тип и событие завершения'],['Подключение',['attachedComponentReadback'],'MonoBehaviour появился на тестовом кубе'],['Откат',['standardRevertConfirmed','sourceRestored'],'Стандартный Revert восстановил отсутствие файла'],['Повторная компиляция',['restoreCompilationFinished','restoreCompilerErrorsAbsent'],'Событие завершения и отсутствие compiler errors'],['Очистка',['ownedFixtureDestroyed','testSceneCleaned'],'Тестовый куб и сохранённая сцена удалены']];
 table(s,[['Этап','Проверки','Фактический результат'],...groups.map(([label,keys,meaning])=>[label,checkGroup(keys),meaning])],187,388,[290,150,712],22);
 text(s,`${codeRun.className}: ${codeRun.passedChecks}/${codeRun.totalChecks} проверок одного LLM-скрипта`,64,595,1152,33,26,{color:C.teal,bold:true});
 text(s,`Отдельно: ${journalRun.passedChecks}/${journalRun.totalChecks} проверок журнала двух синтетических импортов`,64,637,1152,29,23,{color:C.muted});
}
// 17. Native editable chart: contract checks with explicit denominators.
{
 const fullTotal=schema.semantic_total,decoderTotal=schema.decoder.semantic_total;
 const typed=decoderSmoke.complete_typed_responses;
 const s=slide('Типизированный контракт инструментов','Проверка схемы и декодера','Текущая полная JSON Schema и её Ollama-вариант проходят одну матрицу из 33 положительных и отрицательных случаев. Результат — 33/33 для каждой схемы. Вариант декодера удаляет только большие maxLength, сохраняя типы, enum, required и закрытые аргументы; C# сохраняет все ограничения. Две отдельные реальные пробы Qwen3.5:2b возвращают полные ожидаемые JSON create_world/create_object, но относятся к сохранённому экспорту до изменения порядка свойств create_world. Они не выдаются за новый inference текущего экспорта; сцена не исполнялась. Отдельный парный replay 9B сохранил требуемые параметры после перестановки, однако нарушил нормализованные диапазоны и не прошёл полную Draft7-проверку. Успешность свободной беседы проверяется отдельно.'+evidenceNotes('results/editor-schema-verification.json','results/editor-schema-compatibility-smoke.json','results/iterations/chat-agent/schema-before-order/editor-decoder-command-schema.json','results/native-argument-order-replay.json'));
 chart(s,{position:{left:64,top:224,width:760,height:366},categories:[`Полная schema, n=${fullTotal}`,`Ollama decoder, n=${decoderTotal}`],series:[{name:'Пройдено случаев матрицы',values:[schema.semantic_passed,schema.decoder.semantic_passed],fill:C.teal}],hasLegend:false,barOptions:{direction:'column',grouping:'clustered',gapWidth:115},yAxis:{min:0,max:Math.max(fullTotal,decoderTotal)+3,majorUnit:10,numberFormatCode:'0',textStyle:{fontSize:20,fill:C.muted},majorGridlines:{fill:C.gray,width:1}},xAxis:{textStyle:{fontSize:22,fill:C.ink}},dataLabels:{showValue:true,position:'outEnd',numberFormatCode:'0',textStyle:{fontSize:28,bold:true,fill:C.navy}},chartFill:C.bg,plotAreaFill:C.bg});
 text(s,'18 инструментов\n19 ветвей schema',886,202,330,91,29,{bold:true,color:C.navy});
 text(s,`${typed.passed}/${typed.total}`,886,336,330,74,52,{bold:true,color:C.teal});
 text(s,'реальных JSON\nQwen3.5 2B / Ollama\nархивная схема\nбез исполнения сцены',886,427,330,140,24);
 text(s,'Схема задаёт структуру ответа. Runtime проверяет все границы.',64,630,1152,33,24,{color:C.muted});
}
// 18. Keep the v1.0 experiment separate from the new editor agent.
{
 const q=summary.qa;
 const s=slide('Архивный эксперимент RAG версии 1.0','30 вопросов в трёх конфигурациях','Исходный эксперимент NPC-пайплайна версии 1.0: 90 запросов, 89 непустых raw и один HTTP 400. Сгенерированная речь означает совпадение известного факта в speech; доставленная речь — то же совпадение после принятия всего решения диспетчером. n=30 на конфигурацию. Протокол experiment-2026-10-30 сохранён до новых изменений. Эти значения относятся к архивному NPC/RAG-пайплайну. Проверка нормализованных вариантов не является полной оценкой соответствия ответа источникам. Отдельный архив Terrain содержит 40 проверок воспроизводимости и замеры C#-генерации, не задержки LLM.'+evidenceNotes('results/summary.json'));
 chart(s,{position:{left:64,top:188,width:850,height:405},categories:['Qwen 2B без RAG','Qwen 2B с RAG','Cloud 20B с RAG'],series:[{name:'Сгенерированная речь',values:[q['local-no-rag'].fact_match_count,q['local-rag'].fact_match_count,q['cloud-rag'].fact_match_count],fill:C.teal},{name:'Доставленная речь',values:[q['local-no-rag'].delivered_fact_match_count,q['local-rag'].delivered_fact_match_count,q['cloud-rag'].delivered_fact_match_count],fill:C.navy}],barOptions:{direction:'column',grouping:'clustered',gapWidth:85},hasLegend:true,legend:{position:'bottom',textStyle:{fontSize:22}},yAxis:{min:0,max:30,majorUnit:10,numberFormatCode:'0',textStyle:{fontSize:20,fill:C.muted},majorGridlines:{fill:C.gray,width:1}},xAxis:{textStyle:{fontSize:21,fill:C.ink}},dataLabels:{showValue:true,position:'outEnd',numberFormatCode:'0',textStyle:{fontSize:23,bold:true,fill:C.ink}},chartFill:C.bg,plotAreaFill:C.bg});
 text(s,'Retrieval hit@3',952,210,260,40,27,{bold:true,color:C.navy});text(s,'30/30',952,265,260,66,48,{bold:true,color:C.teal});text(s,'у обеих\nRAG-конфигураций',952,341,260,80,24);
 text(s,'Отдельный протокол',952,477,260,40,25,{bold:true,color:C.navy});text(s,'30.10.2026\nNPC / RAG v1.0',952,529,260,75,24);
 text(s,'Сгенерированный факт и фактически доставленный ответ имеют разные показатели',64,625,1152,38,23,{color:C.muted});
}
// 19. Read the final real-model result only after the run has finished.
{
 const s=slide('Реальные последовательные беседы','Приёмка редакторского агента','По одному последовательному разговору из семи заданий в локальной и облачной конфигурации; последующие задачи зависят от предыдущих. Итоги характеризуют конкретные беседы, а рейтинг моделей и вероятность надёжности по ним не выводятся. Протоколы и доли представлены раздельно. Результаты заданы полями passed и observed окончательных JSON. Локальная модель: '+localAcceptance.modelKey+'. Облачная: '+acceptance.modelKey+'. C# draft считается ожидающим просмотра, а его реальный импорт проверен отдельно на слайде 16. '+(isCloudV2?'В cloud-v2 первое создание мира фактически выполнило требования, но последующая проверка сорвалась на malformed JSON; исходный FAIL сохранён. ':'')+(cloudTerrainLexicalFailure?'Облачный шаг 7 упомянул «1 террейн», но словарный автокритерий Terrain не зачёл этот вариант. Счёт 6/7 и исходный FAIL сохранены, сцена не изменена, предыдущий черновик остался на ревью. ':'')+'Снимок меню получен из фактического Unity Editor. Для cloud-v4 отдельная mcs-проверка RotatorY не запускала Unity и не подменяет 11 Unity-проверок другого YAxisRotator. Ручная проверка ограниченных критериев не подтверждает всю речь: утверждение обо всех объектах в пределах координат не доказано, камера находится вне footprint Terrain.'+evidenceNotes(localAcceptancePath,acceptancePath,'docs/assets/screenshots/chat-plus-menu.png','results/chat-code-cloud-v4-source-review.json','results/chat-agent-acceptance-cloud-v4-semantic-review.json'));
 const resultLabel=(items,index)=>{const result=items.find(item=>item.number===index+1);return !result?'Нет итога':result.passed?'Пройдено':index===6&&items===actualCases?'Не зачтён':'Ошибка';};
 const caseRows=Array.from({length:actualPlanned},(_,index)=>[String(index+1),caseNames[index],resultLabel(localCases,index),resultLabel(actualCases,index)]);
 text(s,`Автоприёмка: ${localPassed}/${localCompleted} локально; ${actualPassed}/${actualCompleted} в облаке`,64,181,1152,40,26,{bold:true,color:C.navy});
 table(s,[['№','Проверяемая задача','Локально','Облако'],...caseRows],239,350,[55,435,135,135],21,64,760);
 await image(s,'chat-plus-menu.png',870,239,346,350,{left:.312,top:.59,right:.348,bottom:.106});
 text(s,'Меню задач',870,609,346,30,24,{bold:true,color:C.teal});
 text(s,cloudTerrainLexicalFailure?'Облако, шаг 7: «1 террейн» не зачтён автокритерием.\nСцена не изменена. По одному диалогу.':isCloudV2?'Cloud-v2: мир создан; FAIL последующей проверки ответа.':`Завершено: ${localCompleted}/${actualPlanned} локально, ${actualCompleted}/${actualPlanned} в облаке. По одному диалогу.`,64,613,770,50,20,{color:C.muted});
}
// 20
{
 const s=slide('Результат и границы применения','Заключение','WorldWeaver 1.1 реализует чат на UI Toolkit, 18 именованных инструментов, историю, проверку результата и отдельный цикл просмотра, компиляции и отката C#. Runtime сохраняет TerrainSpec, индивидуальный RAG, NPC и проверяемые игровые действия. Программные тесты, контрактная матрица, две пробы декодирования, 11 проверок одного LLM-скрипта, 34 проверки двух синтетических импортов и две реальные беседы имеют разные единицы измерения. Архивные 90 QA-запросов рассматриваются отдельно от приёмки чата. Пользовательский просмотр C# не создаёт песочницу исполнения кода.'+evidenceNotes('docs/research/chat-agent-test-summary.md',localAcceptancePath,acceptancePath));
 text(s,'Диалоговый инструментарий Unity\nдля создания мира, персонажей и проверяемых изменений',64,188,1152,100,35,{bold:true,color:C.navy});
 text(s,'Подтверждённые результаты версии 1.1',64,326,1152,39,29,{bold:true,color:C.teal});
 text(s,`18 инструментов и сохраняемая история\n${await xmlCounts('chat-agent-editmode.xml')} EditMode, ${await xmlCounts('chat-agent-playmode.xml')} PlayMode\n${codeRun.passedChecks}/${codeRun.totalChecks} одного LLM-скрипта · ${journalRun.passedChecks}/${journalRun.totalChecks} журнала синтетических импортов\nБеседы: ${localPassed}/${localCompleted} локально; ${actualPassed}/${actualCompleted} в облаке`,64,382,1152,146,27);
 text(s,'Границы результата',64,542,1152,39,29,{bold:true,color:C.teal});
 text(s,'Качество зависит от модели и контекста, облако зависит от сервиса.\nПросмотр C# требует решения разработчика перед импортом.',64,594,1152,69,26);
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
 const result=await finalizePresentation({workspaceDir:root,candidatePath:candidate,finalPath,pythonExecutable:path.join(runtime,'python/bin/python3'),integrityValidatorPath:path.join(skill,'container_tools/inspect_presentation_package_integrity.py'),layoutValidatorPath:path.join(skill,'container_tools/inspect_presentation_layout_geometry.py'),layoutArgs:['--expected-slide-size-emu','12192000,6858000','--validate-bullet-geometry','--validate-heading-fit','--require-native-table-slide','5','--require-native-table-slide','6','--require-native-table-slide','15','--require-native-table-slide','16','--require-native-table-slide','19'],explicitTotalSlideCount:20,requiredNativeTableOwnerSlides:[5,6,15,16,19],requiredNativeChartOwnerSlides:[17,18],materializeLiteralChartWorkbooks:true,fontPolicy:{basis:'design',families:[FONT]},verifyArtifactToolImport:true,receiptPath:path.join(out,process.env.WORLDWEAVER_PPTX_RECEIPT??'validation-requests.json')});
 console.log(JSON.stringify(result));
}
console.log(`Rendered ${pres.slides.items.length} slides in ${out}`);
