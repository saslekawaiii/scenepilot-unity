#if ENABLE_INPUT_SYSTEM
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using WorldWeaver.Editor.Assistant;
namespace WorldWeaver.Tests
{
 public sealed class ProbeInputEvidenceTests
 {
  static JObject Evidence(RuntimeProbeContext probe)=>(JObject)typeof(RuntimeProbeContext).GetMethod("InputEvidence",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(probe,null);
  [Test] public void CallbackAssertionsDoNotFabricateDeviceDelivery()
  {
   using(var probe=new RuntimeProbeContext()){probe.Check(true,"callback logic");Assert.AreEqual(0,(int)Evidence(probe)["keyboardStateEvents"]);Assert.AreEqual(0,(int)Evidence(probe)["mouseStateEvents"]);}
  }
  [Test] public void OnlyReceivedStateEventsForOwnedVirtualDevicesAreCounted()
  {
   var other=InputSystem.AddDevice<Keyboard>();
   try
   {
    using(var probe=new RuntimeProbeContext())
    {
     InputSystem.QueueStateEvent(other,new KeyboardState(Key.A));InputSystem.Update();Assert.AreEqual(0,(int)Evidence(probe)["keyboardStateEvents"]);
     InputSystem.QueueStateEvent(probe.Keyboard,new KeyboardState(Key.Space));InputSystem.QueueStateEvent(probe.Mouse,new MouseState{position=new UnityEngine.Vector2(30,20)});InputSystem.Update();
     Assert.AreEqual(1,(int)Evidence(probe)["keyboardStateEvents"]);Assert.AreEqual(1,(int)Evidence(probe)["mouseStateEvents"]);
     probe.Dispose();InputSystem.QueueStateEvent(other,new KeyboardState());InputSystem.Update();Assert.AreEqual(1,(int)Evidence(probe)["keyboardStateEvents"]);
    }
   }
   finally{if(other.added)InputSystem.RemoveDevice(other);}
  }
 }
}
#endif
