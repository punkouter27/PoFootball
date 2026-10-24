using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PoFootball.Tests
{
    /// <summary>
    /// Opens each of the three scenes and looks for the faults that load without
    /// an error and fail later, or never visibly at all:
    ///
    ///   MISSING SCRIPTS — a component whose class was renamed or deleted. Unity
    ///   keeps it as an empty slot and logs one line at load.
    ///
    ///   BROKEN REFERENCES — a serialized object reference that names an object
    ///   that no longer exists: an asset deleted, a GameObject removed. It reads
    ///   as null at runtime, exactly like a slot nobody filled, and the two are
    ///   told apart here by the stored instance id, which only a broken one has.
    ///   UnityEvent targets are serialized references too, so a button wired to a
    ///   deleted object is caught by the same test.
    ///
    /// Additive and closed again, so whatever the Editor had open is left as it
    /// was. Read-only: nothing is saved.
    ///
    /// m_Model on BehaviorParameters is skipped by name. CLAUDE.md records those
    /// scene references as dead by design — Agent_FootballPlayer assigns the model
    /// from Agent_BrainRegistry at Awake over whatever the scene held — and
    /// Tools > PoFootball > Clear Dead m_Model References In Open Scenes is the
    /// tool for them. A missing script found here is what Clean Missing Scripts
    /// In Open Scenes removes.
    /// </summary>
    public sealed class Systems_SceneIntegrityTests
    {
        private static readonly string[] ScenePaths =
        {
            "Assets/Scenes/SCN_MENU.unity",
            "Assets/Scenes/SCN_GAME.unity",
            "Assets/Scenes/SCN_TRAIN_FOOTBALL.unity",
        };

        private static readonly string[] IgnoredProperties =
        {
            "m_Model",
        };

        [Test]
        public void EveryScene_HasNoMissingScriptOrBrokenReference(
            [ValueSource(nameof(ScenePaths))] string path)
        {
            Assert.That(
                AssetDatabase.LoadAssetAtPath<SceneAsset>(path), Is.Not.Null, path + " is not on disk");

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            List<string> faults = new List<string>();

            try
            {
                GameObject[] roots = scene.GetRootGameObjects();

                for (int index = 0; index < roots.Length; index++)
                {
                    Scan(roots[index].transform, faults);
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            Assert.That(faults, Is.Empty, path + ":\n" + string.Join("\n", faults));
        }

        private static void Scan(Transform node, List<string> faults)
        {
            GameObject gameObject = node.gameObject;
            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);

            if (missing > 0)
            {
                faults.Add($"{PathOf(node)}: {missing} missing script(s)");
            }

            Component[] components = gameObject.GetComponents<Component>();

            for (int index = 0; index < components.Length; index++)
            {
                if (components[index] != null)
                {
                    ScanReferences(components[index], node, faults);
                }
            }

            for (int child = 0; child < node.childCount; child++)
            {
                Scan(node.GetChild(child), faults);
            }
        }

        private static void ScanReferences(Component component, Transform node, List<string> faults)
        {
            using (SerializedObject serialized = new SerializedObject(component))
            {
                SerializedProperty property = serialized.GetIterator();

                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference
                        || property.objectReferenceValue != null
                        || property.objectReferenceEntityIdValue.Equals(default(EntityId))
                        || IsIgnored(property.name))
                    {
                        continue;
                    }

                    faults.Add(
                        $"{PathOf(node)}: {component.GetType().Name}.{property.propertyPath} points at an object that no longer exists");
                }
            }
        }

        private static bool IsIgnored(string propertyName)
        {
            for (int index = 0; index < IgnoredProperties.Length; index++)
            {
                if (propertyName == IgnoredProperties[index])
                {
                    return true;
                }
            }

            return false;
        }

        private static string PathOf(Transform node)
        {
            return node.parent == null ? node.name : PathOf(node.parent) + "/" + node.name;
        }
    }
}
