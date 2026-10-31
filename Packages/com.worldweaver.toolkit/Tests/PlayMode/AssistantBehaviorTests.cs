using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace WorldWeaver.Tests
{
    public sealed class AssistantBehaviorTests
    {
        [UnityTest]
        public IEnumerator PatrolPathCanShrinkWhileRunning()
        {
            var go = new GameObject("WorldWeaver patrol regression");
            try
            {
                var behavior = go.AddComponent<SceneBehavior>(); behavior.Mode = SceneBehaviorKind.Patrol;
                behavior.Waypoints = new[] { Vector3.zero, Vector3.zero, Vector3.zero };
                yield return null; yield return null;
                behavior.Waypoints = new[] { Vector3.zero, Vector3.right };
                yield return null; yield return null;
                Assert.That(float.IsNaN(go.transform.position.x), Is.False);
                Assert.That(go.transform.position.x, Is.GreaterThanOrEqualTo(0));
                LogAssert.NoUnexpectedReceived();
            }
            finally { Object.Destroy(go); }
        }
    }
}
