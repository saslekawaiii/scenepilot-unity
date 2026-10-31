using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Resumes explicitly authorized unfinished tasks after real compiler/test jobs and domain reload.</summary>
    [InitializeOnLoad]
    internal static class AgentTaskResumeCoordinator
    {
        static readonly List<WeakReference<ChatController>> controllers=new List<WeakReference<ChatController>>();
        static bool running;
        static double next;
        static AgentTaskResumeCoordinator(){EditorApplication.update+=Tick;}
        internal static void Register(ChatController controller)=>controllers.Add(new WeakReference<ChatController>(controller));
        internal static void Unregister(ChatController controller)=>controllers.RemoveAll(w=>!w.TryGetTarget(out var value)||ReferenceEquals(value,controller));
        static async void Tick()
        {
            if(Application.isBatchMode||running||EditorApplication.timeSinceStartup<next||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||ScriptDrafts.Pending||AgentSourcePatches.Pending||AgentEditorJobs.Busy)return;
            next=EditorApplication.timeSinceStartup+2;
            var live=controllers.Select(w=>w.TryGetTarget(out var c)?c:null).Where(c=>c!=null).ToList();if(live.Any(c=>c.Busy))return;
            var store=new ChatHistoryStore();
            var pending=store.Load().SelectMany(s=>s.Messages.Where(m=>m.Task?.ContinueAfterReload==true&&m.PendingPlan==null).Select(m=>new{Session=s,Message=m})).OrderBy(v=>v.Message.CreatedUtc,StringComparer.Ordinal).FirstOrDefault();
            if(pending==null)return;
            if(pending.Message.Task.ResumeAttempts>=3||pending.Message.Task.Status=="restored")
            {pending.Message.Task.ContinueAfterReload=false;pending.Message.Status="Нужно продолжить вручную";store.Save(pending.Session);foreach(var c in live)c.ReloadFromDisk();return;}
            running=true;ChatController hidden=null;
            try
            {
                var controller=live.FirstOrDefault(c=>c.Current.Id==pending.Session.Id);
                if(controller==null){hidden=new ChatController(historyFolder:Path.GetFullPath("UserSettings/WorldWeaverChat"));hidden.OpenChat(pending.Session.Id);controller=hidden;}
                else controller.ReloadFromDisk();
                await controller.ResumeAsync(pending.Message.Id);
            }
            catch(Exception error)
            {
                var session=store.Load().FirstOrDefault(s=>s.Id==pending.Session.Id);var message=session?.Messages.FirstOrDefault(m=>m.Id==pending.Message.Id);
                if(message!=null){message.Task.ContinueAfterReload=false;message.IsError=true;message.Status="Продолжение остановлено";message.Text+="\n"+ChatHistoryStore.Redact(error.Message);store.Save(session);}
            }
            finally{hidden?.Dispose();running=false;foreach(var c in live)c.ReloadFromDisk();}
        }
    }
}
