using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver.Editor.Assistant;

// Resume the actual interrupted controller state. No scene repairs or supplied plans.
public static class RecoverAgentTaskAcceptanceRunner
{
    static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
    public static async void Run()
    {
        string configPath=Environment.GetEnvironmentVariable("WW_RECOVERY_CONFIG");var config=JObject.Parse(File.ReadAllText(configPath));var report=new JObject{["startedUtc"]=DateTime.UtcNow.ToString("O"),["providerInjected"]=false,["sceneRepairByHarness"]=false,["paidApiUsed"]=false};
        try
        {
            string project=Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName);if(!Application.isBatchMode||Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE")!="1"||project!=(string)config["project"])throw new InvalidOperationException("Explicit isolated project required.");
            string rawPath=(string)config["rawReport"];var original=JObject.Parse(File.ReadAllText(rawPath));if((bool?)original["completed"]!=true)throw new InvalidOperationException("Prior run must have ended.");
            string scene=(string)original["resultScene"],sceneBefore=Hash(scene);var sources=Directory.GetFiles("Assets","*.cs",SearchOption.AllDirectories).ToDictionary(p=>p,Hash);EditorSceneManager.OpenScene(scene);
            string history=(string)original["history"];using(var controller=new ChatController(historyFolder:history,modelConnectionsFolder:Path.Combine(history,"models")))
            {
                string answerId=(string)((JArray)original["messages"]).OfType<JObject>().Last(m=>(string)m["Role"]=="assistant"&&m["Task"] is JObject)["Id"];
                var sourceSession=controller.Sessions.Single(s=>s.Messages.Any(m=>m.Id==answerId));controller.OpenChat(sourceSession.Id);report["loadedProjectChats"]=controller.Sessions.Count;
                controller.Settings.ModelKey=(string)original["modelKey"];controller.Settings.Mode=AssistantMode.Execute;controller.Settings.Power=AssistantPower.Deep;controller.Settings.AutoVerify=true;controller.Settings.AllowAutonomousScripts=true;controller.Settings.UseNativeFunctions=true;
                var answer=controller.Current.Messages.Last(m=>m.Role=="assistant"&&m.Task!=null);if(answer.Task.OriginalRequest!=(string)original["prompt"])throw new InvalidOperationException("Original task differs.");
                int plans=answer.Attempts.Count,steps=answer.Steps.Count,resumes=answer.Task.ResumeAttempts;report["resumeAttemptsBefore"]=resumes;report["originalRawReportSha256"]=Hash(rawPath);report["originalImplementationProof"]=config["originalProof"];
                await controller.ResumeAsync(answer.Id);
                var checks=(JObject)original["checks"].DeepClone();checks.Remove("legacyFlatSurfaceRemoved");
                checks["noError"]=!answer.IsError;checks["noPendingPlan"]=answer.PendingPlan==null;checks["notBlocked"]=answer.Task.Status!="blocked"&&answer.Task.Status!="partial";
                checks["allPlannedStagesReviewed"]=answer.Task.ActiveApproachStage>=answer.Task.Approach.Count;checks["freshWholeTaskReviewPassed"]=answer.EditorReviews.LastOrDefault(r=>r.ApproachStage<0&&r.StepCount==answer.Steps.Count)?.Passed==true;
                foreach(var check in HistoricalSceneAssertions.Evaluate((JObject)config["expectations"]).Properties())checks[check.Name]=check.Value.DeepClone();
                checks["noNewPlannerTurnDuringRecovery"]=answer.Attempts.Count==plans;checks["noNewSceneOperationsDuringRecovery"]=answer.Steps.Skip(steps).All(s=>s.Tool=="job_result");checks["continuationLimitNotRaised"]=answer.Task.ResumeAttempts==resumes;
                checks["sceneUnchangedByRecovery"]=sceneBefore==Hash(scene);checks["sourcesUnchangedByRecovery"]=sources.All(p=>File.Exists(p.Key)&&Hash(p.Key)==p.Value);
                report["checks"]=checks;report["passed"]=checks.Properties().All(p=>(bool)p.Value);report["completed"]=true;report["resumeAttemptsAfter"]=answer.Task.ResumeAttempts;report["additionalPlannerTurns"]=answer.Attempts.Count-plans;report["messages"]=JArray.FromObject(controller.Current.Messages);report["history"]=history;report["prompt"]=original["prompt"];report["model"]=original["model"];report["modelKey"]=original["modelKey"];report["resultScene"]=scene;report["sceneSha256"]=sceneBefore;report["criterionCorrection"]=config["criterionCorrection"].DeepClone();
            }
        }
        catch(Exception error){report["passed"]=false;report["completed"]=true;report["error"]=error.ToString();}
        report["finishedUtc"]=DateTime.UtcNow.ToString("O");File.WriteAllText((string)config["output"],report.ToString());EditorApplication.Exit((bool)report["passed"]?0:1);
    }
}
