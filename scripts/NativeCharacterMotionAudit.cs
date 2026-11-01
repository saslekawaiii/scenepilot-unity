using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using WorldWeaver;

/// <summary>Temporary native QA observer. Never sets transforms, input states or animation parameters.</summary>
[InitializeOnLoad]
public static class NativeCharacterMotionAudit
{
    const string Key="WW.NativeCharacterMotionAudit";
    static double next;
    static NativeCharacterMotionAudit(){EditorApplication.update+=Observe;}
    [MenuItem("Tools/WorldWeaver QA/Character/Start observing native input")]
    static void Start()
    {
        if(!EditorApplication.isPlaying)throw new InvalidOperationException("Enter Play Mode first; this observer never starts gameplay or sends keys.");
        string folder=Path.GetFullPath("UserSettings/WorldWeaverNativeCharacterAudit");Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ")+".jsonl");
        File.WriteAllText(path,new JObject{["kind"]="native-user-project-read-only-motion-observer",["unity"]=Application.unityVersion,["source"]="Assets/WorldWeaverNativeQA/Editor/NativeCharacterMotionAudit.cs",
            ["sourceSha256"]=Hash(File.ReadAllBytes("Assets/WorldWeaverNativeQA/Editor/NativeCharacterMotionAudit.cs")),["inputSynthesizedByObserver"]=false,
            ["projectSettingsActiveInputHandler"]=File.ReadLines("ProjectSettings/ProjectSettings.asset").FirstOrDefault(l=>l.TrimStart().StartsWith("activeInputHandler:",StringComparison.Ordinal))?.Trim(),
            ["packageVersion"]=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WorldPlayer).Assembly).version}.ToString(Newtonsoft.Json.Formatting.None)+"\n");
        SessionState.SetString(Key,path);next=0;Debug.Log("Native motion observer started: "+path);
    }
    [MenuItem("Tools/WorldWeaver QA/Character/Stop observing native input")]
    static void Stop(){string path=SessionState.GetString(Key,"");SessionState.EraseString(Key);Debug.Log("Native motion observer stopped: "+path);}
    static void Observe()
    {
        string path=SessionState.GetString(Key,"");if(string.IsNullOrEmpty(path)||!EditorApplication.isPlaying||EditorApplication.timeSinceStartup<next)return;
        next=EditorApplication.timeSinceStartup+.04;
        var players=UnityEngine.Object.FindObjectsByType<WorldPlayer>(FindObjectsSortMode.None).Where(p=>p.isActiveAndEnabled).ToArray();
        if(players.Length!=1)return;var p=players[0];var b=p.GetComponent<WorldCharacterAnimation>();var c=p.GetComponent<CharacterController>();
        var pose=new JArray(b&&b.Visual?b.Visual.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(r=>r.bones).Where(t=>t).Distinct().Select(t=>new JObject{["name"]=t.name,["rotation"]=new JArray(t.localRotation.x,t.localRotation.y,t.localRotation.z,t.localRotation.w)}):Array.Empty<JObject>());
        var row=new JObject{["utc"]=DateTime.UtcNow.ToString("O"),["time"]=Time.timeAsDouble,["player"]=p.name,["enabled"]=p.enabled,["position"]=new JArray(p.transform.position.x,p.transform.position.y,p.transform.position.z),
            ["moveInput"]=new JArray(WorldInput.Movement.x,WorldInput.Movement.y),["sprintHeld"]=WorldInput.SprintHeld,["jumpPressed"]=WorldInput.JumpPressed,["grounded"]=p.IsGrounded,
            ["speed"]=b?b.MeasuredSpeed:0,["minMoveDistance"]=c?c.minMoveDistance:-1,["clips"]=new JArray(b&&b.Animator?b.Animator.GetCurrentAnimatorClipInfo(0).Select(i=>new JObject{["clip"]=i.clip.name,["weight"]=i.weight}):Array.Empty<JObject>()),["bones"]=pose};
        File.AppendAllText(path,row.ToString(Newtonsoft.Json.Formatting.None)+"\n");
    }
    static string Hash(byte[] bytes){using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
}
