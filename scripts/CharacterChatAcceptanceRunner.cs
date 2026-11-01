using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldWeaver;
using WorldWeaver.Editor.Assistant;

/// <summary>Opt-in isolated acceptance: the real model chooses the tool; a later probe measures real animation and physics.</summary>
[InitializeOnLoad]
public static class CharacterChatAcceptanceRunner
{
    const string Key="WorldWeaver.CharacterAcceptance.Output";
    public const string Prompt="Создай персонажа: сделай главного героя не используй примитив. используй реальную модель. добавь ей анимации ходьбы, передвижения и тд";
    static CharacterChatAcceptanceRunner()
    {
        EditorApplication.playModeStateChanged+=state=>
        {
            string output=SessionState.GetString(Key,"");
            if(state==PlayModeStateChange.EnteredPlayMode&&!string.IsNullOrEmpty(output))
                new GameObject("Disposable character acceptance probe").AddComponent<CharacterAcceptanceMotionProbe>().Begin(output);
        };
    }
    public static async void Run()
    {
        string output=Environment.GetEnvironmentVariable("WW_CHARACTER_OUTPUT");
        var report=new JObject{["kind"]="real-chat-animated-character-and-motion",["prompt"]=Prompt,["startedUtc"]=DateTime.UtcNow.ToString("O"),["passed"]=false,
            ["providerInjected"]=false,["paidApiUsed"]=false,["keyboardInputTested"]=false,["checks"]=new JObject()};
        ChatController controller=null;
        try
        {
            if(!Application.isBatchMode||Environment.GetEnvironmentVariable("WW_CHARACTER_ACCEPTANCE")!="1"||string.IsNullOrEmpty(output))throw new InvalidOperationException("Explicit disposable batch acceptance required.");
            string project=Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName);
            if(project!=Path.GetFullPath(Environment.GetEnvironmentVariable("WW_CHARACTER_PROJECT")??""))throw new InvalidOperationException("Wrong disposable project.");
            if(File.Exists(output))throw new IOException("Refusing to overwrite a receipt.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            string history=Path.Combine(project,"UserSettings/CharacterAcceptance",Guid.NewGuid().ToString("N"));
            controller=new ChatController(historyFolder:history,modelConnectionsFolder:Path.Combine(history,"models"));
            string model=Environment.GetEnvironmentVariable("WW_CHARACTER_MODEL")??"ollama:qwen3.5:9b";
            if(model.StartsWith("ollama:",StringComparison.Ordinal))await controller.RefreshLocalModelsAsync();
            controller.Settings.ModelKey=model;controller.Settings.Mode=AssistantMode.Execute;controller.Settings.Power=AssistantPower.Fast;
            controller.Settings.AutoApply=true;controller.Settings.AutoVerify=true;controller.SaveSettings();
            report["modelKey"]=model;report["power"]="Fast";report["historyFolder"]=history;
            report["activeInputHandler"]=File.ReadLines("ProjectSettings/ProjectSettings.asset").FirstOrDefault(line=>line.TrimStart().StartsWith("activeInputHandler:",StringComparison.Ordinal))?.Trim();
            string package=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WorldPlayer).Assembly).resolvedPath;
            var files=new JObject();foreach(string path in Directory.GetFiles(package,"*",SearchOption.AllDirectories).OrderBy(p=>p,StringComparer.Ordinal))
                if(Path.GetFileName(path)!=".DS_Store")files[path.Substring(package.Length+1).Replace('\\','/')]=Hash(File.ReadAllBytes(path));
            report["packageFilesBefore"]=files;report["harnessSha256"]=Hash(File.ReadAllBytes("Assets/Editor/CharacterChatAcceptanceRunner.cs"));
            controller.Changed+=()=>{report["messages"]=JArray.FromObject(controller.Current.Messages);Write(output,report);};
            Application.runInBackground=true;
            await controller.SendAsync(Prompt);
            var answer=controller.Current.Messages.LastOrDefault(m=>m.Role=="assistant");report["messages"]=JArray.FromObject(controller.Current.Messages);
            var checks=(JObject)report["checks"];
            checks["realModelAttempt"]=answer!=null&&answer.Attempts.Count>0;
            checks["noAssistantError"]=answer!=null&&!answer.IsError;
            checks["noPendingCodeApproved"]=answer!=null&&answer.PendingPlan==null&&!ScriptDrafts.Pending&&!AgentSourcePatches.Pending;
            checks["actualCharacterToolChosen"]=answer!=null&&answer.Steps.Any(s=>s.Success&&s.Tool=="create_animated_player");
            checks["goalActuallyVerified"]=answer?.Task?.Checks.Any(c=>c.Passed&&answer.Task.Requirements.Any(r=>r.Kind=="animated_player"&&r.Id==c.RequirementId))==true;
            checks["fastDeadlineMet"]=answer!=null&&answer.ElapsedMs<120000;
            var players=UnityEngine.Object.FindObjectsByType<WorldPlayer>(FindObjectsSortMode.None);
            checks["oneActualPlayer"]=players.Length==1;
            bool unchanged=files.Properties().All(p=>File.Exists(Path.Combine(package,p.Name))&&Hash(File.ReadAllBytes(Path.Combine(package,p.Name)))==(string)p.Value);
            checks["packageSourcePreserved"]=unchanged;
            if(!checks.Properties().All(p=>(bool)p.Value))throw new InvalidOperationException("Chat acceptance failed; preserved full raw attempts in receipt.");
            Directory.CreateDirectory("Assets/CharacterChatAcceptance");AssetDatabase.Refresh();AssetDatabase.SaveAssets();
            string scene="Assets/CharacterChatAcceptance/"+Path.GetFileNameWithoutExtension(output)+".unity";if(!EditorSceneManager.SaveScene(players[0].gameObject.scene,scene))throw new IOException("Cannot save generated scene.");
            report["scene"]=scene;report["chatElapsedMs"]=answer.ElapsedMs;Write(output,report);
            controller.Dispose();controller=null;
            SessionState.SetString(Key,output);EditorApplication.isPlaying=true;
        }
        catch(Exception error)
        {
            report["error"]=error.ToString();if(!string.IsNullOrEmpty(output))Write(output,report);
            controller?.Dispose();SessionState.EraseString(Key);EditorApplication.Exit(1);
        }
    }
    public static void RunMotion()
    {
        string source=Environment.GetEnvironmentVariable("WW_CHARACTER_MOTION_SOURCE"),output=Environment.GetEnvironmentVariable("WW_CHARACTER_OUTPUT");
        if(!Application.isBatchMode||Environment.GetEnvironmentVariable("WW_CHARACTER_ACCEPTANCE")!="1"||string.IsNullOrEmpty(source)||File.Exists(output))throw new InvalidOperationException("Explicit new disposable motion receipt required.");
        var report=JObject.Parse(File.ReadAllText(source));report["replayedGeneratedSceneFrom"]=source;report["replayedReceiptSha256"]=Hash(File.ReadAllBytes(source));report["motionReplayHarnessSha256"]=Hash(File.ReadAllBytes("Assets/Editor/CharacterChatAcceptanceRunner.cs"));report["passed"]=false;report.Remove("motion");
        foreach(string key in new[]{"idleIsStill","walkMovesAndDeformsBones","sprintFasterAndDeformsBones","jumpRises","landsOnPhysicalGround"})((JObject)report["checks"]).Remove(key);
        report["motionReplayNoNewInference"]=true;EditorSceneManager.OpenScene((string)report["scene"]);Write(output,report);SessionState.SetString(Key,output);EditorApplication.isPlaying=true;
    }
    internal static void Write(string output,JObject report){Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,report.ToString());}
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    internal static void Finish(){SessionState.EraseString(Key);}
}

public sealed class CharacterAcceptanceMotionProbe:MonoBehaviour
{
    public void Begin(string output){StartCoroutine(Run(output));}
    IEnumerator Run(string output)
    {
        var report=JObject.Parse(File.ReadAllText(output));var checks=(JObject)report["checks"];
        // EnteredPlayMode can run before the scene's first normal Update. Let lifecycle and physics initialize first.
        yield return null;yield return new WaitForFixedUpdate();yield return null;yield return new WaitForSeconds(1);
        var player=UnityEngine.Object.FindFirstObjectByType<WorldPlayer>();var binder=player?player.GetComponent<WorldCharacterAnimation>():null;
        if(!player||!binder){report["error"]="No actual player/binder after entering Play Mode.";CharacterChatAcceptanceRunner.Write(output,report);CharacterChatAcceptanceRunner.Finish();EditorApplication.Exit(1);yield break;}
        player.enabled=false;player.View.enabled=true;Application.runInBackground=true;
        var samples=new JArray();report["motion"]=samples;
        var bones=player.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(r=>r.bones).Where(b=>b).Distinct().ToArray();
        foreach(string phase in new[]{"settle","idle","walk","sprint","jump","land"})
        {
            float duration=phase=="settle"?.5f:phase=="jump"?.35f:phase=="land"?1.5f:.8f;
            float end=Time.time+duration;var start=player.transform.position;float maxY=start.y,maxPose=0;
            int frameCount=0;float maxDt=0,integratedDt=0;
            var pose=bones.Select(b=>b.localRotation).ToArray();bool jumped=false;
            while(Time.time<end)
            {
                bool jump=phase=="jump"&&!jumped;jumped|=jump;
                frameCount++;maxDt=Mathf.Max(maxDt,Time.deltaTime);integratedDt+=Mathf.Min(Time.deltaTime,.1f);
                player.MoveFrame(phase=="walk"||phase=="sprint"?Vector2.up:Vector2.zero,phase=="sprint",jump,Time.deltaTime);
                maxY=Mathf.Max(maxY,player.transform.position.y);
                for(int i=0;i<bones.Length;i++)maxPose=Mathf.Max(maxPose,Quaternion.Angle(pose[i],bones[i].localRotation));
                yield return null;
            }
            var clipWeights=new JArray(binder.Animator.GetCurrentAnimatorClipInfo(0).Select(c=>new JObject{["clip"]=c.clip.name,["weight"]=c.weight}));
            var sample=new JObject{["phase"]=phase,["frames"]=frameCount,["maxFrameDelta"]=maxDt,["movementIntegratedSeconds"]=integratedDt,["distance"]=Vector3.Distance(start,player.transform.position),["rise"]=maxY-start.y,["bonePoseMaxDegrees"]=maxPose,["measuredSpeed"]=binder.MeasuredSpeed,["grounded"]=player.IsGrounded,["clips"]=clipWeights};
            samples.Add(sample);
            if(phase=="idle")checks["idleIsStill"]=(float)sample["distance"]<.08f;
            if(phase=="walk")checks["walkMovesAndDeformsBones"]=(float)sample["distance"]>1&&maxPose>5&&clipWeights.OfType<JObject>().Any(c=>((string)c["clip"]).ToLowerInvariant().Contains("walk")&&(float)c["weight"]>.7f);
            if(phase=="sprint")checks["sprintFasterAndDeformsBones"]=(float)sample["distance"]>2.5f&&maxPose>5&&clipWeights.OfType<JObject>().Any(c=>((string)c["clip"]).ToLowerInvariant().Contains("sprint")&&(float)c["weight"]>.7f);
            if(phase=="jump")checks["jumpRises"]=(float)sample["rise"]>.45f;
            if(phase=="land")checks["landsOnPhysicalGround"]=player.IsGrounded;
            if(phase=="idle"||phase=="walk"||phase=="sprint")
            {
                var camera=player.View;var texture=new RenderTexture(960,600,24);camera.targetTexture=texture;camera.Render();
                var previous=RenderTexture.active;RenderTexture.active=texture;var image=new Texture2D(960,600,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,600),0,0);image.Apply();
                string screenshot=Path.ChangeExtension(output,null)+"-"+phase+".png";File.WriteAllBytes(screenshot,image.EncodeToPNG());sample["cameraRender"]=screenshot;
                camera.targetTexture=null;RenderTexture.active=previous;Destroy(image);texture.Release();Destroy(texture);
            }
            CharacterChatAcceptanceRunner.Write(output,report);
        }
        report["motionInputScope"]="Deterministic calls to the same WorldPlayer.MoveFrame used by keyboard Update. Player input Update disabled during measurements; real CharacterController physics, LateUpdate animation binding, Animator clips, bones and camera rendering remained active. This is not a native keyboard acceptance.";
        report["passed"]=checks.Properties().All(p=>(bool)p.Value);report["finishedUtc"]=DateTime.UtcNow.ToString("O");
        CharacterChatAcceptanceRunner.Write(output,report);CharacterChatAcceptanceRunner.Finish();EditorApplication.Exit((bool)report["passed"]?0:1);
    }
}
