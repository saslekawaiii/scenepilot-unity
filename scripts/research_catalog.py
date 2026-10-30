"""Build an auditable primary-source catalogue and check HTTP availability."""
import concurrent.futures
import json
import ssl
import urllib.error
import urllib.request
from pathlib import Path
import certifi

SOURCES = [
('Unity 6000.3.27f1 Release Notes','Unity Technologies','2026','https://unity.com/releases/editor/whats-new/6000.3.27f1','Выбор конкретной версии Unity 6 LTS','3.1'),
('Git dependencies Unity Manual','Unity Technologies','2022–2026','https://docs.unity3d.com/2022.3/Documentation/Manual/upm-git.html','Установка UPM-пакета из Git с параметром path','2.2'),
('TerrainData SetHeights','Unity Technologies','2026','https://docs.unity3d.com/6000.3/Documentation/ScriptReference/TerrainData.SetHeights.html','Heightmap и нормированные высоты Terrain','3.3'),
('NavMeshBuilder BuildNavMeshData','Unity Technologies','2026','https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AI.NavMeshBuilder.BuildNavMeshData.html','Построение навигационной сетки из выбранной геометрии','3.7'),
('Configure AI Gateway Assistant 2.9','Unity Technologies','2026','https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.9/manual/integration/ai-gateway-get-started.html','Gateway поддерживает сторонних агентов и требует соответствующего доступа организации','1.5'),
('Configure character actions','Convai','2026','https://github.com/Conv-AI/Convai-Documentation/blob/main/plugins-and-integrations/convai-unity-sdk/features/character-actions/configuring-actions.md','Convai предоставляет действия персонажей, доступные цели и локальное исполнение','1.5'),
('Inworld Controller Unity Runtime','Inworld AI','2026','https://dev.docs.inworld.ai/Unity/runtime/runtime-reference/InworldController','Модули LLM, речи, embeddings и knowledge в Unity runtime','1.5'),
('NVIDIA ACE for Games','NVIDIA','2026','https://developer.nvidia.com/ace-for-games','Game Agent SDK содержит Agent, Chat и RAG API; on-device стек имеет собственные аппаратные требования','1.5'),
('Promethean AI','Promethean AI','2026','https://www.prometheanai.com/','Работа с 3D-ассетами и компоновкой окружения; основная интеграция Unreal, доступны плагины других редакторов','1.5'),
('Procedural Worlds','Procedural Worlds','2026','https://www.procedural-worlds.com/','Продукты процедурного создания Unity-окружений Gaia и смежные инструменты','1.5'),
('LLMUnity','UndreamAI','2026','https://github.com/undreamai/LLMUnity','Локальная LLM-интеграция, диалоговые персонажи и RAG в Unity','1.5'),
('LangChain knowledge base','LangChain','2026','https://github.com/langchain-ai/docs/blob/main/src/oss/langchain/knowledge-base.mdx','Разделение документов, vector store и retrieval','2.5'),
('Using VectorStoreIndex','LlamaIndex','2026','https://developers.llamaindex.ai/python/framework/module_guides/indexing/vector_store_index/','Построение векторного индекса и извлечение фрагментов','2.5'),
('Retrieval-Augmented Generation for Knowledge-Intensive NLP Tasks','Lewis P., Perez E., Piktus A. et al.','2020','https://arxiv.org/abs/2005.11401','Соединение параметрической модели и извлекаемой внешней памяти','1.3'),
('Generative Agents Interactive Simulacra of Human Behavior','Park J. S., O’Brien J. C., Cai C. J. et al.','2023','https://arxiv.org/abs/2304.03442','Архитектура памяти, извлечения и планирования поведения агентов','1.4'),
('ReAct Synergizing Reasoning and Acting in Language Models','Yao S., Zhao J., Yu D. et al.','2022–2023','https://arxiv.org/abs/2210.03629','Связь модельных решений с ограниченным набором действий среды','1.4'),
('Structured outputs Ollama','Ollama','2026','https://docs.ollama.com/capabilities/structured-outputs','JSON schema для локального вывода; ограничения отдельно от бизнес-валидации','3.4'),
('Qwen3.5 2B Ollama model card','Ollama and Qwen','2026','https://ollama.com/library/qwen3.5:2b','Характеристики выбранной небольшой локальной модели и вариантов весов','3.1'),
('EmbeddingGemma Ollama model card','Ollama and Google','2025–2026','https://ollama.com/library/embeddinggemma','300M embedding-модель, многоязычный текст и локальный запуск','3.5'),
('EmbeddingGemma inference','Google AI for Developers','2026','https://ai.google.dev/gemma/docs/embeddinggemma/inference-embeddinggemma-with-sentence-transformers','Различие префиксов query/document при поиске','3.5'),
('Chat API reference','OpenAI','2026','https://developers.openai.com/api/reference/resources/chat','Структура chat completion, usage и response format','3.4'),
('Structured Outputs Groq','Groq','2026','https://console.groq.com/docs/structured-outputs','GPT-OSS 20B/120B в списке поддерживаемых structured output моделей; доступность проверяется API','3.4'),
('Pollinations Classic API','Pollinations','2026','https://github.com/pollinations/pollinations/blob/master/APIDOCS.md','Анонимный бесплатный API, GPT-OSS 20B, один текстовый запрос раз в 30 секунд, best effort','3.4'),
('Unity Test Framework 1.1','Unity Technologies','2026','https://docs.unity3d.com/Packages/com.unity.test-framework@1.1/manual/index.html','EditMode/PlayMode и запуск автоматических испытаний','4.2'),
('Undo RegisterCompleteObjectUndo','Unity Technologies','2026','https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Undo.RegisterCompleteObjectUndo.html','Undo при обновлении собственного TerrainData','3.2'),
('JSON Schema Draft 2020 12','JSON Schema','2020','https://json-schema.org/draft/2020-12','Описание структуры ответа независимо от семантики действий','2.7'),
('Бланки учебно методического управления ВолгГТУ','ВолгГТУ','2019–2026','https://www.vstu.ru/obrazovanie/umu/blanki/','Официальный бланк пояснительной записки к выпускной работе бакалавра','Оформление'),
('ГОСТ 19.201-78 Техническое задание Требования к содержанию и оформлению','Росстандарт','1978','https://protect.gost.ru/gost/details/b64061cf-6bb2-4c75-aee9-18bb5e26e554','Структура ТЗ программного изделия, не универсальный стандарт всех разделов ВКР','Приложение А'),
('ГОСТ 7.32-2017 Отчет о научно исследовательской работе','Росстандарт','2017','https://protect.gost.ru/gost/details/7d280e43-7036-4a69-8e6e-d15867028343','Ориентир структуры и оформления отчётной текстовой части при отсутствии более конкретной методички','Оформление'),
('ГОСТ Р 7.0.100-2018 Библиографическая запись Библиографическое описание','Росстандарт','2018','https://protect.gost.ru/gost/details/389dfd5a-535a-458a-81c3-14b729b1cee1','Описание источников; стандарт прямо не регулирует правила внутритекстовых ссылок','Библиография'),
('MCP for Unity','CoplayDev','2026','https://github.com/CoplayDev/unity-mcp','Автоматизация редактора отличается от поведения runtime NPC','1.5'),
('Procedural Content Generation in Games','Shaker N., Togelius J., Nelson M. J.','2016','https://pcgbook.com/','Алгоритмическое создание игрового содержимого и оценка процедурной генерации','1.1'),
('LM Studio OpenAI compatibility','LM Studio','2026','https://lmstudio.ai/docs/developer/openai-compat','Альтернативный локальный OpenAI-compatible сервер','2.9'),
('llama.cpp','ggml-org','2026','https://github.com/ggml-org/llama.cpp','Локальный inference и HTTP server как альтернативный runtime','2.9')
]

def build():
    rows=[dict(id=i+1,title=s[0],author=s[1],year=s[2],url=s[3],conclusion=s[4],section=s[5],accessed='2026-10-30') for i,s in enumerate(SOURCES)]
    ctx=ssl.create_default_context(cafile=certifi.where())
    def check(row):
        try:
            req=urllib.request.Request(row['url'],headers={'User-Agent':'Mozilla/5.0 (academic source verification)'})
            with urllib.request.urlopen(req,context=ctx,timeout=25) as r: row['http_status']=r.status;row['resolved_url']=r.url
        except urllib.error.HTTPError as e:row['http_status']=e.code
        except Exception as e:row['http_status']=type(e).__name__
        return row
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:rows=list(pool.map(check,rows))
    Path('docs/research/sources.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2)+'\n')
    text='# Проверенные источники\n\nДата доступа — 30 октября 2026 года. HTTP-проверка фиксирует доступность, а не доказывает корректность каждого вывода. Динамические продукты сопоставлялись по опубликованной документации, а не по полному практическому испытанию аналогов.\n\n'
    for r in rows:text+=f"## [{r['id']}] {r['title']}\n\n{r['author']}. {r['year']}. {r['url']}\n\nПроверка: {r['http_status']}. Вывод: {r['conclusion']}. Раздел ВКР: {r['section']}.\n\n"
    Path('docs/research/sources.md').write_text(text)
    print('SOURCES',len(rows),'HTTP_200',sum(r['http_status']==200 for r in rows),'REQUIRES_REVIEW',[(r['id'],r['http_status']) for r in rows if r['http_status']!=200])

if __name__=='__main__':build()
