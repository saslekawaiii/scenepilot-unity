using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace WorldWeaver
{
    /// <summary>Procedural bone locomotion, not authored clips. Uses the imported
    /// Humanoid mapping, without assuming a preset's bone names or a Mecanim graph.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldHumanoidLocomotion:MonoBehaviour
    {
        public Animator Animator;
        public Transform Actor;
        public float MeasuredSpeed {get;private set;}
        readonly Dictionary<HumanBodyBones,Transform> bones=new Dictionary<HumanBodyBones,Transform>();
        readonly Dictionary<HumanBodyBones,Quaternion> rest=new Dictionary<HumanBodyBones,Quaternion>();
        Vector3 previous;
        float phase;
        bool HasAuthoredController=>Animator&&Animator.runtimeAnimatorController&&Animator.runtimeAnimatorController.animationClips.Length>0;
        void Start()
        {
            if(!Animator)Animator=GetComponent<Animator>();if(!Actor)Actor=transform.parent;
            if(!Actor||!Animator||!Animator.avatar||!Animator.avatar.isValid||!Animator.avatar.isHuman||HasAuthoredController){enabled=false;return;}
            var hierarchy=Animator.GetComponentsInChildren<Transform>(true);var mapping=Animator.avatar.humanDescription.human;
            foreach(var id in new[]{HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.LeftUpperArm,HumanBodyBones.RightUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.RightLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.RightHand})
            {
                string name=mapping.FirstOrDefault(h=>h.humanName==id.ToString()).boneName;
                var bone=string.IsNullOrEmpty(name)?null:hierarchy.FirstOrDefault(t=>t.name==name);
                if(bone){bones[id]=bone;rest[id]=bone.localRotation;}
            }
            if(!bones.ContainsKey(HumanBodyBones.LeftUpperLeg)||!bones.ContainsKey(HumanBodyBones.RightUpperLeg)){enabled=false;Debug.LogWarning("Procedural locomotion needs exposed Humanoid leg transforms; use a compatible controller for an optimized rig.",this);return;}
            LowerArm(HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand);LowerArm(HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand);
            previous=Actor.position;
        }
        void LowerArm(HumanBodyBones upper,HumanBodyBones lower,HumanBodyBones hand)
        {
            if(!bones.TryGetValue(upper,out var arm)||!bones.TryGetValue(lower,out var elbow))return;
            Vector3 direction=elbow.position-arm.position;if(direction.sqrMagnitude<.0001f)return;
            arm.rotation=Quaternion.FromToRotation(direction.normalized,-Actor.up)*arm.rotation;rest[upper]=arm.localRotation;
            if(bones.TryGetValue(hand,out var wrist))
            {
                Vector3 forearm=wrist.position-elbow.position;
                if(forearm.sqrMagnitude>.0001f)elbow.rotation=Quaternion.FromToRotation(forearm.normalized,(-Actor.up+transform.forward*.12f).normalized)*elbow.rotation;
                rest[lower]=elbow.localRotation;
            }
        }
        void Swing(HumanBodyBones id,float degrees)
        {
            if(!bones.TryGetValue(id,out var bone)||!bone||!bone.parent)return;
            Vector3 axis=bone.parent.InverseTransformDirection(transform.right).normalized;
            bone.localRotation=Quaternion.AngleAxis(degrees,axis)*rest[id];
        }
        void LateUpdate()
        {
            if(!Actor||bones.Count==0||Time.deltaTime<=0||HasAuthoredController)return;
            Vector3 delta=Actor.position-previous;previous=Actor.position;delta.y=0;
            MeasuredSpeed=delta.sqrMagnitude<16?Mathf.Min(20,delta.magnitude/Time.deltaTime):0;
            // The gait plane follows the visual's facing, including strafing and
            // walking backwards relative to the actor/camera. Actor.right caused
            // sideways knees and twisted arm swing after those turns.
            if(MeasuredSpeed>.08f)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(delta.normalized,Vector3.up),540*Time.deltaTime);
            float stride=Mathf.Clamp01(MeasuredSpeed/2.2f);phase+=Time.deltaTime*9*stride;float swing=Mathf.Sin(phase)*stride;
            Swing(HumanBodyBones.LeftUpperLeg,28*swing);Swing(HumanBodyBones.RightUpperLeg,-28*swing);
            Swing(HumanBodyBones.LeftLowerLeg,5+Mathf.Max(0,-swing)*38);Swing(HumanBodyBones.RightLowerLeg,5+Mathf.Max(0,swing)*38);
            Swing(HumanBodyBones.LeftUpperArm,-14*swing);Swing(HumanBodyBones.RightUpperArm,14*swing);
            Swing(HumanBodyBones.LeftLowerArm,-8);Swing(HumanBodyBones.RightLowerArm,-8);
        }
    }
}
