using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Real rendered images kept outside Assets; only explicit attachments enter model requests.</summary>
    public static class AgentViewCapture
    {
        const string Folder="UserSettings/WorldWeaverImages";
        static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        public static ChatAttachment Import(string path)
        {
            if(string.IsNullOrWhiteSpace(path)||!File.Exists(path)||!new[]{".png",".jpg",".jpeg"}.Contains(Path.GetExtension(path).ToLowerInvariant()))throw new ArgumentException("Select an existing PNG/JPEG image.");
            if(new FileInfo(path).Length>8*1024*1024)throw new ArgumentException("Image must be <=8MiB.");
            var texture=new Texture2D(2,2,TextureFormat.RGB24,false);
            try
            {
                if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(path),false)||texture.width>8192||texture.height>8192||(long)texture.width*texture.height>32_000_000)throw new ArgumentException("Unsupported or oversized image.");
                var bytes=texture.EncodeToPNG();
                // Normalize only the managed attachment copy; the selected source file is never rewritten.
                while(texture.width>1280||texture.height>1280||bytes.Length>2*1024*1024)
                {
                    float ratio=Mathf.Min(.8f,1280f/Mathf.Max(texture.width,texture.height));
                    int width=Mathf.Max(64,Mathf.RoundToInt(texture.width*ratio)),height=Mathf.Max(64,Mathf.RoundToInt(texture.height*ratio));
                    if(width>=texture.width&&height>=texture.height)throw new ArgumentException("Image cannot fit the attachment limit.");
                    var pixels=texture.GetPixels32();var resized=new Color32[width*height];
                    for(int y=0;y<height;y++)for(int x=0;x<width;x++)resized[y*width+x]=pixels[Mathf.Min(texture.height-1,y*texture.height/height)*texture.width+Mathf.Min(texture.width-1,x*texture.width/width)];
                    var next=new Texture2D(width,height,TextureFormat.RGB24,false);next.SetPixels32(resized);next.Apply();Object.DestroyImmediate(texture);texture=next;bytes=texture.EncodeToPNG();
                }
                return Save(bytes,Path.GetFileName(path));
            }
            finally{Object.DestroyImmediate(texture);}
        }
        static ChatAttachment Save(byte[] bytes,string name)
        {
            string hash=Hash(bytes);Directory.CreateDirectory(Folder);string path=Folder+"/"+hash+".png";
            if(!File.Exists(path))File.WriteAllBytes(path,bytes);
            return new ChatAttachment{Path=path,Name=name,Sha256=hash};
        }
        public static ChatAttachment Capture(int width=960,int height=540)
        {
            if(width<320||width>1600||height<180||height>1200)throw new ArgumentException("Screenshot dimensions out of bounds.");
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)throw new InvalidOperationException("Rendering is unavailable in a -nographics editor. No screenshot was produced.");
            Camera source=Camera.main;
            if(!source)source=ProjectContextIndex.Objects().Select(go=>go.GetComponent<Camera>()).FirstOrDefault(c=>c&&c.enabled);
            if(!source&&SceneView.lastActiveSceneView)source=SceneView.lastActiveSceneView.camera;
            if(!source)throw new ArgumentException("No scene/game camera available for capture.");
            var holder=new GameObject("WorldWeaver capture camera"){hideFlags=HideFlags.HideAndDontSave};
            var rt=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
            var previous=RenderTexture.active;Texture2D texture=null;
            try
            {
                var camera=holder.AddComponent<Camera>();camera.CopyFrom(source);holder.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);camera.enabled=false;camera.targetTexture=rt;camera.Render();
                RenderTexture.active=rt;texture=new Texture2D(width,height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();
                return Save(texture.EncodeToPNG(),"Сцена "+DateTime.Now.ToString("HH:mm:ss"));
            }
            finally{RenderTexture.active=previous;if(texture)Object.DestroyImmediate(texture);RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(holder);}
        }
        public static string ReadBase64(ChatAttachment attachment)
        {
            string full=Path.GetFullPath(attachment.Path??"");string root=Path.GetFullPath(Folder)+Path.DirectorySeparatorChar;
            if(!full.StartsWith(root,StringComparison.Ordinal)||!File.Exists(full)||new FileInfo(full).Length>2*1024*1024)throw new ArgumentException("Attachment is outside the managed image store or exceeds2MiB.");
            var bytes=File.ReadAllBytes(full);if(Hash(bytes)!=attachment.Sha256)throw new ArgumentException("Image changed after it was attached.");return Convert.ToBase64String(bytes);
        }
        public static JObject Describe(ChatAttachment attachment)=>JObject.FromObject(new{attachment.Path,attachment.Name,attachment.Sha256,attachment.Kind});
    }
}
