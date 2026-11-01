using NUnit.Framework;
using UnityEngine;

namespace WorldWeaver.Tests
{
    public sealed class WorldPlayerMovementTests
    {
        [Test] public void TinyWalkingStepsPreserveDistanceAndGroundContact()
        {
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Disposable movement floor";
            floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(20,1,20);
            var hero=new GameObject("High frame rate player");hero.transform.position=Vector3.up*.05f;
            try
            {
                var motor=hero.AddComponent<CharacterController>();motor.height=1.8f;motor.center=new Vector3(0,.9f,0);motor.radius=.3f;motor.minMoveDistance=.001f;
                var player=hero.AddComponent<WorldPlayer>();player.Speed=2.2f;player.enabled=false;Physics.SyncTransforms();
                for(int i=0;i<40;i++)player.MoveFrame(Vector2.zero,false,false,1f/60);
                Assert.IsTrue(player.IsGrounded,"The actual capsule must settle on the physical floor before walking.");
                Vector3 before=hero.transform.position;
                for(int i=0;i<1000;i++)player.MoveFrame(Vector2.up,false,false,.0001f);
                Assert.That(hero.transform.position.z-before.z,Is.EqualTo(.22f).Within(.01f),"Small frame steps must accumulate, not be discarded by minMoveDistance.");
                Assert.That(Mathf.Abs(hero.transform.position.y-before.y),Is.LessThan(motor.skinWidth+.01f),"Capsule skin separation is allowed; falling or repeated upward drift is not.");
                Assert.IsTrue(player.IsGrounded,"Tiny gravity moves must still report physical ground contact.");
            }
            finally{Object.DestroyImmediate(hero);Object.DestroyImmediate(floor);}
        }
    }
}
