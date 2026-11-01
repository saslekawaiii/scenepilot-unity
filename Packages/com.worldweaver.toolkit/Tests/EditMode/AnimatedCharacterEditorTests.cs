using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WorldWeaver.Tests
{
    public sealed class AnimatedCharacterEditorTests
    {
        private Type tools, transactionType;
        private object transaction;
        private int undoGroup;
        [SetUp] public void Setup()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var assembly = typeof(Editor.Assistant.SceneToolExecutor).Assembly;
            tools = assembly.GetType("WorldWeaver.Editor.Assistant.AnimatedCharacterTools", true);
            transactionType = assembly.GetType("WorldWeaver.Editor.Assistant.SceneBuildTransaction", true);
            transaction = Activator.CreateInstance(transactionType, true);
            Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
        }
        [TearDown] public void Cleanup()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Undo.RevertAllDownToGroup(undoGroup);
            transactionType.GetMethod("RollbackAssets", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(transaction, null);
        }
        private object Call(string name, params object[] args) => tools.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        private GameObject Create(string preset, string target = "")
        {
            var command = new Editor.Assistant.EditorCommand { tool = "create_animated_player", name = "Animated hero", target = target, args = new JObject { ["preset"] = preset, ["position"] = new JArray(0, 1, 0) } };
            var result = Call("Execute", command, transaction, null, (Func<string, GameObject>)Editor.Assistant.SceneToolExecutor.ResolveTarget, null);
            return (GameObject)result.GetType().GetField("Target", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(result);
        }

        [TestCase("adventurer")]
        [TestCase("explorer")]
        public void ImportedPresetHasRealSkinnedGeometryAndFiveAuthoredMotionClips(string preset)
        {
            var hero = Create(preset);
            var report = (JObject)Call("Inspect", hero);
            Assert.That((bool)report["success"], Is.True, report.ToString());
            Assert.That((int)report["vertices"], Is.GreaterThan(100));
            Assert.That((int)report["bones"], Is.GreaterThanOrEqualTo(7));
            Assert.That(hero.GetComponentsInChildren<SkinnedMeshRenderer>().Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(hero.GetComponentsInChildren<MeshFilter>(), Is.Empty, "Do not silently replace the requested model with primitive mesh renderers.");
            Assert.That(hero.GetComponent<WorldPlayer>().View, Is.Not.Null);
            var binding = hero.GetComponent<WorldCharacterAnimation>();
            var controller = (AnimatorController)binding.Animator.runtimeAnimatorController;
            Assert.That(controller.parameters.Select(p => p.name), Does.Contain("Speed").And.Contain("Grounded"));
            var machine = controller.layers[0].stateMachine;
            var tree = machine.defaultState.motion as BlendTree;
            Assert.That(tree, Is.Not.Null);
            Assert.That(tree.children.Length, Is.EqualTo(3));
            CollectionAssert.AreEquivalent(new[] { "idle", "walk", "sprint" }, tree.children.Select(c => ((AnimationClip)c.motion).name.ToLowerInvariant()));
            foreach (var child in tree.children)
            {
                var clip = (AnimationClip)child.motion;
                Assert.That(clip.length, Is.GreaterThan(.1));
                Assert.That(AnimationUtility.GetCurveBindings(clip).Length, Is.GreaterThan(0), clip.name + " must contain actual authored bone curves.");
                Assert.That(AnimationUtility.GetAnimationClipSettings(clip).loopTime, Is.True, clip.name + " must loop.");
            }
        }

        [Test] public void ReplacesOnlyPreviousOwnedAppearanceAndReusesPlayerCameraAndInventory()
        {
            var first = Create("adventurer");
            var camera = first.GetComponent<WorldPlayer>().View;
            var inventory = first.GetComponent<Inventory>();
            var second = Create("explorer", "id:" + first.GetInstanceID());
            Assert.That(second, Is.SameAs(first));
            Assert.That(UnityEngine.Object.FindObjectsByType<WorldPlayer>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(first.GetComponent<WorldPlayer>().View, Is.SameAs(camera));
            Assert.That(first.GetComponent<Inventory>(), Is.SameAs(inventory));
            Assert.That(first.GetComponentsInChildren<Animator>().Length, Is.EqualTo(1));
            Assert.That((bool)((JObject)Call("Inspect", first))["success"], Is.True);
        }

        [Test] public void UnknownPresetFailsBeforeCreatingPlayerOrGeneratedAssets()
        {
            var error = Assert.Throws<TargetInvocationException>(() => Create("invented-photorealistic-model"));
            Assert.That(error.InnerException, Is.TypeOf<ArgumentException>());
            Assert.That(UnityEngine.Object.FindObjectsByType<WorldPlayer>(FindObjectsSortMode.None), Is.Empty);
            var assets = (List<string>)transactionType.GetField("Assets", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(transaction);
            Assert.That(assets, Is.Empty);
        }

        [Test] public void UserOwnedTargetFailsBeforeSceneMutation()
        {
            var existing = new GameObject("Hand made scene object");
            var error = Assert.Throws<TargetInvocationException>(() => Create("adventurer", "id:" + existing.GetInstanceID()));
            Assert.That(error.InnerException, Is.TypeOf<ArgumentException>());
            Assert.That(existing.transform.childCount, Is.EqualTo(0));
            Assert.That(UnityEngine.Object.FindObjectsByType<WorldPlayer>(FindObjectsSortMode.None), Is.Empty);
        }
    }
}
