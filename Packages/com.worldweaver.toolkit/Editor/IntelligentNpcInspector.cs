using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace WorldWeaver.Editor
{
    /// <summary>Small per-character authoring surface. No inference, downloads or credential storage occur in the Inspector.</summary>
    [CustomEditor(typeof(IntelligentNPC))]
    public sealed class IntelligentNpcInspector : UnityEditor.Editor
    {
        bool advanced,socialSettings;
        static readonly string[] MainFields={"NpcName","Portrait","Role","Personality","Biography","Goals","Constraints","Models"};
        public override void OnInspectorGUI()
        {
            var npc=(IntelligentNPC)target;serializedObject.Update();
            EditorGUILayout.LabelField("Интеллект персонажа",EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("aiEnabled"),new GUIContent("Включить ИИ","Свободные ответы выбранной модели. Квестовые действия доступны отдельно."));
            if(EditorGUI.EndChangeCheck())
            {
                bool enabled=serializedObject.FindProperty("aiEnabled").boolValue;serializedObject.ApplyModifiedProperties();
                if(Application.isPlaying)npc.AiEnabled=enabled;
                EditorUtility.SetDirty(npc);serializedObject.Update();
            }
            if(Application.isPlaying)EditorGUILayout.HelpBox(npc.AiStatus,MessageType.Info);
            EditorGUILayout.Space(4);EditorGUILayout.LabelField("Личность",EditorStyles.boldLabel);
            foreach(string name in MainFields.Take(7))EditorGUILayout.PropertyField(serializedObject.FindProperty(name),true);
            using(new EditorGUI.DisabledScope(npc.Busy))
            {
                EditorGUI.BeginChangeCheck();EditorGUILayout.PropertyField(serializedObject.FindProperty("Models"),new GUIContent("Модели персонажа"));
                bool routeChanged=EditorGUI.EndChangeCheck();serializedObject.ApplyModifiedProperties();
                if(routeChanged)npc.ReleaseServices();
            }
            if(npc.AiEnabled&&!npc.Models)EditorGUILayout.HelpBox("Назначьте Orchestrator Profile с маршрутами Dialogue и Planning. В чате можно написать: «Включи ИИ у этого NPC и используй выбранную модель».",MessageType.Warning);
            EditorGUILayout.HelpBox("В игре E открывает разговор, ответ можно написать своими словами. Письмо и награда выдаются отдельной кнопкой по делу.",MessageType.None);
            DrawSocial(npc);
            DrawIdentity(npc);
            advanced=EditorGUILayout.Foldout(advanced,"Дополнительные настройки",true);
            if(advanced)
            {
                serializedObject.Update();
                DrawPropertiesExcluding(serializedObject,new[]{"m_Script","aiEnabled","CharacterId","conversationHistory","persistentMemory"}.Concat(MainFields).ToArray());
                using(new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("CharacterId"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("persistentMemory"),new GUIContent("Сохранённые эпизоды"),true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("conversationHistory"),new GUIContent("История реплик"),true);
                }
                EditorGUILayout.LabelField("Эпизоды памяти",npc.ExportMemory().Count+" / 32");
                EditorGUILayout.LabelField("Реплики",npc.ConversationHistory.Count+" / 32");
                serializedObject.ApplyModifiedProperties();
            }
        }
        void DrawSocial(IntelligentNPC npc)
        {
            EditorGUILayout.Space(5);EditorGUILayout.LabelField("Жизнь рядом с другими NPC",EditorStyles.boldLabel);
            var social=npc.GetComponent<NpcSocialAutonomy>();bool enabled=social&&social.enabled&&social.Enabled;
            using(new EditorGUI.DisabledScope(npc.Busy))
            {
                EditorGUI.BeginChangeCheck();bool next=EditorGUILayout.Toggle(new GUIContent("Разговаривать с соседями","Персонажи могут подходить друг к другу по NavMesh и обсуждать свои цели."),enabled);
                if(EditorGUI.EndChangeCheck())
                {
                    if(next&&!social)social=Undo.AddComponent<NpcSocialAutonomy>(npc.gameObject);
                    if(social) {Undo.RecordObject(social,"Configure NPC social AI");social.Enabled=next;social.enabled=next;EditorUtility.SetDirty(social);}
                }
            }
            if(!social)return;
            if(Application.isPlaying)EditorGUILayout.LabelField("Сейчас",social.Status,EditorStyles.wordWrappedLabel);
            socialSettings=EditorGUILayout.Foldout(socialSettings,"Настройки разговоров с соседями",true);
            if(!socialSettings)return;
            var state=new SerializedObject(social);state.Update();
            foreach(string name in new[]{"Topic","CooldownSeconds","MaxTurns","InteractionRadius","ApproachNearbyCharacters","SearchRadius"})EditorGUILayout.PropertyField(state.FindProperty(name),true);
            state.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("До 3 реплик по умолчанию и не более 4 фоновых запросов в минуту на весь запуск. Игрок получает приоритет. Для ходьбы необходимы NavMesh и NavMeshAgent; участники должны принадлежать одному миру или общей корневой группе.",MessageType.None);
        }
        void DrawIdentity(IntelligentNPC npc)
        {
            bool duplicate=npc.gameObject.scene.IsValid()&&!string.IsNullOrWhiteSpace(npc.CharacterId)&&Resources.FindObjectsOfTypeAll<IntelligentNPC>().Count(other=>other&&other.gameObject.scene==npc.gameObject.scene&&other.CharacterId==npc.CharacterId)>1;
            if(!duplicate&&!string.IsNullOrWhiteSpace(npc.CharacterId))return;
            EditorGUILayout.HelpBox("Для отдельной памяти каждого NPC требуется уникальный CharacterId. Пустой или повторяющийся ID мешает разделять разговоры.",MessageType.Warning);
            bool hasMemory=npc.ExportMemory().Count>0||npc.ConversationHistory.Count>0;
            using(new EditorGUI.DisabledScope(npc.Busy||hasMemory))if(GUILayout.Button("Назначить уникальный ID"))
            {
                Undo.RecordObject(npc,"Assign NPC identity");npc.ReleaseServices();npc.CharacterId=Guid.NewGuid().ToString("N");EditorUtility.SetDirty(npc);serializedObject.Update();
            }
            if(hasMemory)EditorGUILayout.HelpBox("У этого персонажа уже есть память. Создайте нового NPC с новым ID, чтобы сохранить историю существующего персонажа.",MessageType.None);
        }
    }
}
