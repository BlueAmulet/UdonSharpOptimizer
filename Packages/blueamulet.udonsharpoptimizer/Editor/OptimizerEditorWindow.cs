using UnityEditor;
using UnityEngine;

#pragma warning disable IDE0017 // Simplify object initialization
#pragma warning disable IDE0090 // Use 'new(...)'

namespace UdonSharpOptimizer
{
    internal class OptimizerEditorWindow : EditorWindow
    {
        OptimizerSettings _settings;
        SerializedObject _settingsSO;
        SerializedProperty _optEnabled;
        SerializedProperty _optCopyAndLoad;
        SerializedProperty _optCopyAndTest;
        SerializedProperty _optStoreAndCopy;
        SerializedProperty _optDoubleCopy;
        SerializedProperty _optCleanUnreadCopy;
        SerializedProperty _optTCO;
        SerializedProperty _optVariables;
        SerializedProperty _optBlockReduction;
        SerializedProperty _optStoreLoad;
        SerializedProperty _optThis;

        [MenuItem("Tools/UdonSharp Optimizer")]
        public static void ShowWindow()
        {
            EditorWindow.GetWindow<OptimizerEditorWindow>("UdonSharp Optimizer");
        }

        public void OnEnable()
        {
            _settings = OptimizerSettings.Instance;
            _settingsSO = new SerializedObject(_settings);
            _optEnabled = _settingsSO.FindProperty(nameof(OptimizerSettings.EnableOptimizer));
            _optCopyAndLoad = _settingsSO.FindProperty(nameof(OptimizerSettings.CopyAndLoad));
            _optCopyAndTest = _settingsSO.FindProperty(nameof(OptimizerSettings.CopyAndTest));
            _optStoreAndCopy = _settingsSO.FindProperty(nameof(OptimizerSettings.StoreAndCopy));
            _optDoubleCopy = _settingsSO.FindProperty(nameof(OptimizerSettings.DoubleCopy));
            _optCleanUnreadCopy = _settingsSO.FindProperty(nameof(OptimizerSettings.CleanUnreadCopy));
            _optTCO = _settingsSO.FindProperty(nameof(OptimizerSettings.EnableTCO));
            _optVariables = _settingsSO.FindProperty(nameof(OptimizerSettings.EnableVariableReduction));
            _optBlockReduction = _settingsSO.FindProperty(nameof(OptimizerSettings.EnableBlockReduction));
            _optStoreLoad = _settingsSO.FindProperty(nameof(OptimizerSettings.EnableStoreLoad));
            _optThis = _settingsSO.FindProperty(nameof(OptimizerSettings.EnableThisBugFix));
        }

        public void OnGUI()
        {
            // TODO: How to properly do this?
            if (_settingsSO == null)
            {
                OnEnable();
            }

            GUIStyle richStyle = new GUIStyle(EditorStyles.label);
            richStyle.richText = true;

            // Optimizer status
            EditorGUILayout.LabelField("Status:", EditorStyles.boldLabel);
            AlignedText("Optimizer:", $"<color={(OptimizerInject.PatchSuccess ? "lime>Activated" : "orange><b>Failed to inject</b>")}</color>", richStyle);
            int patchFailures = OptimizerInject.PatchFailures;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Patches:");
            if (patchFailures == 0)
            {
                EditorGUILayout.LabelField($"<color=lime>{patchFailures} patch failures</color>", richStyle);
            }
            else
            {
                EditorGUILayout.LabelField($"<color=orange><b>{patchFailures} patch failures</b></color>", richStyle);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            // Settings
            EditorGUILayout.LabelField("Settings:", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_optEnabled, false);
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!_settings.EnableOptimizer))
            {
                EditorGUILayout.PropertyField(_optCopyAndLoad, false);
                EditorGUILayout.PropertyField(_optCopyAndTest, false);
                EditorGUILayout.PropertyField(_optStoreAndCopy, false);
                EditorGUILayout.PropertyField(_optDoubleCopy, false);
                EditorGUILayout.PropertyField(_optCleanUnreadCopy, false);
                EditorGUILayout.PropertyField(_optTCO, false);
                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(_optVariables, false);
                using (new EditorGUI.DisabledScope(!_settings.EnableVariableReduction))
                {
                    EditorGUILayout.PropertyField(_optBlockReduction, false);
                    EditorGUILayout.PropertyField(_optStoreLoad, false);
                    EditorGUILayout.PropertyField(_optThis, false);
                }
            }
            if (EditorGUI.EndChangeCheck())
            {
                _settingsSO.ApplyModifiedProperties();
            }

            // Last Build information
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Last Build:", EditorStyles.boldLabel);
            AlignedText("Instructions:", $"{Optimizer.RemovedInstructions} removed", EditorStyles.label);
            AlignedText("Variables:", $"{Optimizer.RemovedVariables} removed", EditorStyles.label);
            AlignedText("Extra __this:", $"{Optimizer.RemovedThisTotal} removed", EditorStyles.label);
        }

        private static void AlignedText(string prefix, string text, GUIStyle style)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(prefix);
            EditorGUILayout.LabelField(text, style);
            EditorGUILayout.EndHorizontal();
        }
    }
}