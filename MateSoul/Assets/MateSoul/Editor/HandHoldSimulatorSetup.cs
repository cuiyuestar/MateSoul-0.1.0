using System;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace MateSoul.Editor
{
    public static class HandHoldSimulatorSetup
    {
        public const string SettingsPath =
            "Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset";
        private const string PackageName = "com.unity.xr.interaction.toolkit";
        private const string PackageVersion = "2.5.4";
        private const string SampleName = "XR Device Simulator";
        private const string PendingKey = "MateSoul.HandHoldSimulatorSetup.Pending";
        private const string PendingRunKey = "MateSoul.HandHoldSimulatorSetup.PendingRun";
        private const string RuntimeInstanceName =
            "XR Device Simulator (Hand Hold Test)";

        public static string SimulatorPrefabPath
        {
            get
            {
                return TryFindSample(out var sample)
                    ? FileUtil.GetProjectRelativePath(
                        sample.importPath + "/XR Device Simulator.prefab")
                    : string.Empty;
            }
        }

        [InitializeOnLoadMethod]
        private static void ResumePendingSetup()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            if (SessionState.GetBool(PendingKey, false))
                EditorApplication.delayCall += CompleteSetup;
            if (SessionState.GetBool(PendingRunKey, false))
                EditorApplication.update += BeginPlay;
        }

        [MenuItem("MateSoul/Game/Set Up Hand Hold Simulator %#i")]
        public static void SetUp()
        {
            if (!TryFindSample(out var sample))
                throw new InvalidOperationException(
                    "XR Interaction Toolkit Device Simulator sample is unavailable.");
            SessionState.SetBool(PendingKey, true);
            if (!sample.isImported && !sample.Import(
                    Sample.ImportOptions.OverridePreviousImports |
                    Sample.ImportOptions.HideImportWindow))
            {
                SessionState.SetBool(PendingKey, false);
                throw new InvalidOperationException(
                    "XR Device Simulator sample import failed.");
            }
            AssetDatabase.Refresh();
            EditorApplication.delayCall += CompleteSetup;
        }

        [MenuItem("MateSoul/Game/Run Hand Hold Simulator %#k")]
        public static void Run()
        {
            SessionState.SetBool(PendingRunKey, true);
            if (!IsConfigured())
            {
                SetUp();
                return;
            }
            OpenSceneAndPlay();
        }

        private static void CompleteSetup()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += CompleteSetup;
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SimulatorPrefabPath);
            var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SettingsPath);
            if (prefab == null || !prefab.TryGetComponent<XRDeviceSimulator>(out _) ||
                settings == null)
                throw new InvalidOperationException(
                    "XR Device Simulator assets are incomplete after import.");

            var serialized = new SerializedObject(settings);
            serialized.FindProperty("m_AutomaticallyInstantiateSimulatorPrefab").boolValue = false;
            serialized.FindProperty("m_AutomaticallyInstantiateInEditorOnly").boolValue = true;
            serialized.FindProperty("m_SimulatorPrefab").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            GameSceneBuilder.BuildScene();
            EditorSceneManager.OpenScene(GameSceneBuilder.ScenePath);
            SessionState.SetBool(PendingKey, false);
            Debug.Log($"MATESOUL_HAND_HOLD_SIMULATOR_READY prefab={SimulatorPrefabPath}");
            if (SessionState.GetBool(PendingRunKey, false))
                EditorApplication.update += BeginPlay;
        }

        private static bool IsConfigured()
        {
            var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SettingsPath);
            if (settings == null) return false;
            var serialized = new SerializedObject(settings);
            return !serialized.FindProperty(
                        "m_AutomaticallyInstantiateSimulatorPrefab").boolValue &&
                   serialized.FindProperty(
                       "m_AutomaticallyInstantiateInEditorOnly").boolValue &&
                   serialized.FindProperty("m_SimulatorPrefab").objectReferenceValue != null;
        }

        private static void OpenSceneAndPlay()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetBool(PendingRunKey, false);
                return;
            }
            GameSceneBuilder.BuildScene();
            EditorSceneManager.OpenScene(GameSceneBuilder.ScenePath);
            EditorApplication.update -= BeginPlay;
            EditorApplication.update += BeginPlay;
        }

        private static void BeginPlay()
        {
            if (!SessionState.GetBool(PendingRunKey, false))
            {
                EditorApplication.update -= BeginPlay;
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            EditorApplication.update -= BeginPlay;
            SessionState.SetBool(PendingRunKey, false);
            EnsureSimulatorInstance();
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            foreach (var simulator in UnityEngine.Object.FindObjectsOfType<XRDeviceSimulator>(true))
            {
                if (simulator.name == RuntimeInstanceName)
                    UnityEngine.Object.DestroyImmediate(simulator.gameObject);
            }
        }

        private static void EnsureSimulatorInstance()
        {
            if (UnityEngine.Object.FindObjectOfType<XRDeviceSimulator>() != null) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SimulatorPrefabPath);
            if (prefab == null)
                throw new InvalidOperationException(
                    "XR Device Simulator prefab is unavailable in PlayMode.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = RuntimeInstanceName;
            instance.tag = "EditorOnly";
            Debug.Log("MATESOUL_HAND_HOLD_SIMULATOR_RUNNING");
        }

        private static bool TryFindSample(out Sample result)
        {
            foreach (var sample in Sample.FindByPackage(PackageName, PackageVersion))
            {
                if (sample.displayName != SampleName) continue;
                result = sample;
                return true;
            }
            result = default;
            return false;
        }
    }
}
