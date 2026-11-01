using System.Linq;
using UnityEngine;

namespace WorldWeaver
{
    [RequireComponent(typeof(WorldPlayer))]
    public sealed class WorldSurvival : MonoBehaviour
    {
        public WorldEnvironment Environment;
        public Transform Habitat;
        public Vector3 HabitatSize=new Vector3(8,4,8);
        public Vector3 Spawn;
        public float OxygenSeconds=180;
        public SurvivalState State = new SurvivalState();
        public bool Pressurized { get; private set; }
        public bool Covered { get; private set; }
        WorldPlayer player;
        WorldCave[] caves;
        WorldResourceNode[] resources;
        public string InteractionHint { get; private set; }
        void Start()
        {
            player=GetComponent<WorldPlayer>();
            caves=Environment?Environment.GetComponentsInChildren<WorldCave>():new WorldCave[0];
            resources=Environment?Environment.GetComponentsInChildren<WorldResourceNode>():new WorldResourceNode[0];
        }
        void Update()
        {
            EvaluateZones(); State.Tick(Time.deltaTime,Pressurized,Covered,OxygenSeconds);
            if(player)player.MovementLocked=!State.Alive;
            var nearby=NearestResource();
            InteractionHint=nearby ? (nearby.IsSample?"E · собрать лунный образец":"E · взять кислород") : "";
        }
        public void EvaluateZones()
        {
            var local=Habitat?Habitat.InverseTransformPoint(transform.position):Vector3.one*10000;
            Pressurized=Habitat && Mathf.Abs(local.x)<HabitatSize.x/2 && Mathf.Abs(local.z)<HabitatSize.z/2 && local.y>=-.3f && local.y<HabitatSize.y;
            Covered=Pressurized || (caves??new WorldCave[0]).Any(c=>c&&c.Contains(transform.position));
        }
        WorldResourceNode NearestResource() => (resources??new WorldResourceNode[0]).Where(r=>r&&!r.Collected&&Vector3.Distance(r.transform.position,transform.position)<=3).OrderBy(r=>(r.transform.position-transform.position).sqrMagnitude).FirstOrDefault();
        public bool TryInteract() { var node=NearestResource();return node&&node.Collect(this); }
        public void Respawn()
        {
            var motor=GetComponent<CharacterController>(); bool enabledMotor=motor&&motor.enabled;
            if(motor)motor.enabled=false;transform.position=Spawn;if(enabledMotor)motor.enabled=true;
            State=new SurvivalState(); if(player)player.MovementLocked=false;
            if(resources!=null)foreach(var node in resources.Where(n=>n))
            {node.Collected=false;foreach(var r in node.GetComponentsInChildren<Renderer>())r.enabled=true;foreach(var c in node.GetComponentsInChildren<Collider>())c.enabled=true;}
        }
        GUIStyle title,small;
        void OnGUI()
        {
            if(!Application.isPlaying || (player&&player.Conversation&&player.Conversation.IsOpen))return;
            if(title==null){title=new GUIStyle(GUI.skin.label){fontSize=20,fontStyle=FontStyle.Bold};small=new GUIStyle(GUI.skin.label){fontSize=13,wordWrap=true};}
            float w=Mathf.Min(360,Screen.width-32);
            GUI.Box(new Rect(16,16,w,172),GUIContent.none);
            GUI.Label(new Rect(32,27,w-32,28),Environment&&Environment.Profile=="moon"?"ЛУННАЯ ЭКСПЕДИЦИЯ":"ЭКСПЕДИЦИЯ",title);
            GUI.Label(new Rect(32,62,w-32,24),$"O₂  {State.Oxygen:0}%     Здоровье  {State.Health:0}%",small);
            GUI.Label(new Rect(32,87,w-32,22),$"Образцы  {State.Samples}/3  ·  Воздействие  {State.Exposure:0}%",small);
            GUI.Label(new Rect(32,114,w-32,55),Pressurized?"База · кислород и здоровье восстанавливаются":Covered?"Пещера · защита от воздействия. Кислород расходуется.":"Соберите 3 образца и вернитесь на базу. Следите за O₂.",small);
            if(!string.IsNullOrEmpty(InteractionHint))GUI.Box(new Rect(Screen.width/2-150,Screen.height-90,300,34),InteractionHint);
            if(!State.Alive || State.Completed)
            {
                GUI.Box(new Rect(Screen.width/2-190,Screen.height/2-65,380,140),State.Alive?"Экспедиция завершена · 3 образца доставлены":"Кислород исчерпан · экспедиция остановлена");
                if(GUI.Button(new Rect(Screen.width/2-125,Screen.height/2,250,40),"Новая экспедиция"))Respawn();
            }
        }
        void OnDisable(){if(player)player.MovementLocked=false;}
    }
}
