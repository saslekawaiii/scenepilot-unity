using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>A human-facing browser sharing the agent's exact search/import/apply pipeline.</summary>
    public sealed class WorldMaterialWindow:EditorWindow
    {
        private string query="дерево",status="13 встроенных поверхностей доступны без интернета.";
        private int source;private Vector2 scroll;private MaterialDescriptor[] materials=Array.Empty<MaterialDescriptor>();
        private CancellationTokenSource operation;private bool children=true,importing;private float tiling=1;
        [MenuItem("Tools/WorldWeaver/Материалы и текстуры",priority=4)]
        public static void Open(){var window=GetWindow<WorldMaterialWindow>();window.titleContent=new GUIContent("Материалы");window.minSize=new Vector2(490,420);window.Show();}
        private void OnEnable(){materials=WorldMaterialLibrary.BuiltIns;WorldMaterialLibrary.TransferProgress+=ShowTransfer;}
        private void OnDisable(){operation?.Cancel();WorldMaterialLibrary.TransferProgress-=ShowTransfer;}
        private void ShowTransfer(MaterialTransferProgress progress)
        {
            if(!importing||!this)return;
            status="Загружаю "+progress.File+": "+progress.Bytes/1024+" / "+progress.TotalBytes/1024+" КиБ"+(progress.RangeFallback?" · восстановление соединения":"");Repaint();
        }
        private void OnGUI()
        {
            EditorGUILayout.Space(12);GUILayout.Label("Материалы для вашего мира",EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Встроенные PBR-поверхности и бесплатные CC0-текстуры из Poly Haven.",EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(8);
            using(new EditorGUILayout.HorizontalScope())
            {
                query=EditorGUILayout.TextField(query);source=EditorGUILayout.Popup(source,new[]{"Везде","Встроенные","Poly Haven"},GUILayout.Width(125));
                using(new EditorGUI.DisabledScope(operation!=null||string.IsNullOrWhiteSpace(query)))if(GUILayout.Button("Найти",GUILayout.Width(65)))_=Search();
            }
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUI.DisabledScope(operation!=null))if(GUILayout.Button("Показать все встроенные")){materials=WorldMaterialLibrary.BuiltIns;status="Встроенная библиотека · 13 оригинальных CC0-материалов · 39 карт.";}
                if(GUILayout.Button("Powered by Poly Haven",EditorStyles.linkLabel))Application.OpenURL("https://polyhaven.com/our-api");
            }
            EditorGUILayout.Space(6);
            using(new EditorGUILayout.HorizontalScope())
            {
                children=EditorGUILayout.ToggleLeft("Включая дочерние Renderer",children,GUILayout.Width(230));
                GUILayout.Label("Повтор",GUILayout.Width(47));tiling=EditorGUILayout.Slider(tiling,.1f,12);
            }
            EditorGUILayout.LabelField("Выбрано: "+(Selection.activeGameObject?Selection.activeGameObject.name:"выберите объект или Terrain в Hierarchy"),EditorStyles.miniLabel);
            scroll=EditorGUILayout.BeginScrollView(scroll);
            foreach(var item in materials)
            {
                using(new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    Rect preview=GUILayoutUtility.GetRect(88,88,GUILayout.Width(88),GUILayout.Height(88));
                    if(item.Id.StartsWith("builtin:",StringComparison.Ordinal))GUI.DrawTexture(preview,WorldMaterialLibrary.BuiltInTexture(item.Id.Substring(8),"albedo"),ScaleMode.ScaleToFit);
                    else EditorGUI.HelpBox(preview,"Poly Haven\nCC0 · 1k\nPBR",MessageType.None);
                    using(new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(item.Name,EditorStyles.boldLabel);EditorGUILayout.LabelField(item.Id,EditorStyles.miniLabel);
                        EditorGUILayout.LabelField(item.Source+" · CC0",EditorStyles.miniLabel);
                        using(new EditorGUILayout.HorizontalScope())
                        {
                            using(new EditorGUI.DisabledScope(operation!=null))if(GUILayout.Button("Импортировать"))_=Import(item,false);
                            using(new EditorGUI.DisabledScope(operation!=null||!Selection.activeGameObject))if(GUILayout.Button("Применить к выбранному"))_=Import(item,true);
                        }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.HelpBox(status,MessageType.Info);
            if(operation!=null&&GUILayout.Button("Отменить загрузку"))operation.Cancel();
            EditorGUILayout.LabelField("Интернет: только 1k PNG/JPEG · проверка хешей · до 24 МиБ на материал",EditorStyles.miniLabel);
        }
        private async Task Search()
        {
            operation=new CancellationTokenSource();status="Ищу текстуры…";Repaint();
            try
            {
                var result=await WorldMaterialLibrary.SearchAsync(query,new[]{"any","builtin","polyhaven"}[source],8,operation.Token);
                if(!this)return;materials=result.Materials.ToArray();status=string.IsNullOrEmpty(result.Warning)?"Найдено: "+materials.Length+". Powered by Poly Haven.":result.Warning;
            }
            catch(OperationCanceledException){if(this)status="Поиск отменён.";}
            catch(Exception error){if(this)status=error.Message;}
            finally{operation.Dispose();operation=null;if(this)Repaint();}
        }
        private async Task Import(MaterialDescriptor item,bool apply)
        {
            var target=Selection.activeGameObject;bool include=children;float repeat=tiling;
            operation=new CancellationTokenSource();importing=true;status="Импортирую "+item.Name+"…";Repaint();
            try
            {
                var imported=await WorldMaterialLibrary.ImportAsync(item.Id,true,operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                if(apply)
                {
                    if(!target)throw new InvalidOperationException("Выбранный объект больше не существует.");
                    var result=WorldMaterialLibrary.Apply(target,imported.MaterialId,repeat,repeat,include);
                    status="Применено: "+result.AppliedRenderers+" Renderer, "+result.AppliedTerrainLayers+" TerrainLayer. "+imported.Warning;
                }
                else{Selection.activeObject=AssetDatabase.LoadAssetAtPath<Material>(imported.MaterialPath);status="Материал импортирован. "+imported.Warning;}
            }
            catch(OperationCanceledException){if(this)status="Загрузка отменена. Сцена не изменена.";}
            catch(Exception error){if(this)status=error.Message;}
            finally{operation.Dispose();operation=null;importing=false;if(this)Repaint();}
        }
    }
}
