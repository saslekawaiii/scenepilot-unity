using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ReviewHistoricalSceneAcceptance
{
    static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
    public static void Run()
    {
        string configPath=Environment.GetEnvironmentVariable("WW_HISTORICAL_REVIEW_CONFIG");var config=JObject.Parse(File.ReadAllText(configPath));var report=new JObject{["sceneRepairByHarness"]=false,["startedUtc"]=DateTime.UtcNow.ToString("O")};
        try
        {
            string project=Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName);if(!Application.isBatchMode||Environment.GetEnvironmentVariable("WW_AGENT_ACCEPTANCE")!="1"||project!=(string)config["project"])throw new InvalidOperationException("Explicit isolated result required.");
            string rawPath=(string)config["rawReport"];var raw=JObject.Parse(File.ReadAllText(rawPath));if((bool?)raw["completed"]!=true)throw new InvalidOperationException("The live run is not complete.");
            string scene=(string)raw["resultScene"],before=Hash(scene);var sourceBefore=Directory.GetFiles("Assets","*.cs",SearchOption.AllDirectories).ToDictionary(p=>p,Hash);EditorSceneManager.OpenScene(scene);
            var checks=(JObject)raw["checks"].DeepClone();
            foreach(string required in new[]{"allPlannedStagesReviewed","freshWholeTaskReviewPassed","actualPlayerInputChecked","actualRelaxedArmsChecked","measuredHeightmapApplied"})if(checks[required]?.Type!=JTokenType.Boolean)throw new InvalidOperationException("Missing live acceptance criterion: "+required);
            if(checks["legacyFlatSurfaceRemoved"]?.Type!=JTokenType.Boolean)throw new InvalidOperationException("This correction applies only to the retained legacy report.");
            var invalid=checks["legacyFlatSurfaceRemoved"].DeepClone();checks.Remove("legacyFlatSurfaceRemoved");
            var historical=HistoricalSceneAssertions.Evaluate((JObject)config["expectations"]);foreach(var check in historical.Properties())checks[check.Name]=check.Value.DeepClone();
            checks["sceneUnchangedByReview"]=before==Hash(scene);checks["sourcesUnchangedByReview"]=sourceBefore.All(p=>File.Exists(p.Key)&&Hash(p.Key)==p.Value);
            report["rawReportSha256"]=Hash(rawPath);report["rawOriginalPassed"]=raw["passed"];report["originalInvalidCriterionValue"]=invalid;report["criterionCorrection"]=config["criterionCorrection"].DeepClone();report["checks"]=checks;report["passed"]=checks.Properties().All(p=>(bool)p.Value);report["scene"]=scene;report["sceneSha256"]=before;report["expectationEvidenceSha256"]=Hash(configPath);
        }
        catch(Exception error){report["passed"]=false;report["error"]=error.ToString();}
        report["finishedUtc"]=DateTime.UtcNow.ToString("O");File.WriteAllText((string)config["output"],report.ToString());EditorApplication.Exit((bool)report["passed"]?0:1);
    }
}
