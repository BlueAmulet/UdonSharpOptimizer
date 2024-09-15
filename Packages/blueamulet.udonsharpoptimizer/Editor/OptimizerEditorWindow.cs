using UnityEditor;
using UnityEngine;

namespace UdonSharpOptimizer
{
    internal class OptimizerEditorWindow : EditorWindow
    {
        OptimizerSettings _settings;
        SerializedObject _settingsSO;
        SerializedProperty _optimizerEnabled;
        SerializedProperty _optimization1;
        SerializedProperty _optimization2;
        SerializedProperty _optimization3;
        SerializedProperty _optimization4;
        SerializedProperty _optimizationVar;
        SerializedProperty _optimizationBR;
        SerializedProperty _optimizationSL;
        SerializedProperty _optimizationThis;

        [MenuItem("Tools/UdonSharp Optimizer")]
        public static void ShowWindow()
        {
            EditorWindow.GetWindow<OptimizerEditorWindow>("UdonSharp Optimizer");
        }

        public void OnEnable()
        {
            _settings = OptimizerSettings.Instance;
            _settingsSO = new SerializedObject(_settings);
            _optimizerEnabled = _settingsSO.FindProperty(nameof(OptimizerSettings.EnableOptimizer));
            _optimization1 = _settingsSO.FindProperty(nameof(OptimizerSettings.EnableOPT01));
            _optimization2 = _settingsSO.FindProperty(nameof(OptimizerSettings.EnableOPT02));
            _optimization3 = _settingsSO.FindProperty(nameof(OptimizerSettings.EnableOPT03));
            _optimization4 = _settingsSO.FindProperty(nameof(OptimizerSettings.EnableOPT04));
            _optimizationVar = _settingsSO.FindProperty(nameof(OptimizerSettings.EnableVariableReduction));
            _optimizationBR = _settingsSO.FindProperty(nameof(OptimizerSettings.EnableBlockReduction));
            _optimizationSL = _settingsSO.FindProperty(nameof(OptimizerSettings.EnableStoreLoad));
            _optimizationThis = _settingsSO.FindProperty(nameof(OptimizerSettings.EnableThisBugFix));
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
            int patchFailures = Optimizer.PatchFailures;
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
            EditorGUILayout.PropertyField(_optimizerEnabled, false);
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!_settings.EnableOptimizer))
            {
                EditorGUILayout.PropertyField(_optimization1, false);
                EditorGUILayout.PropertyField(_optimization2, false);
                EditorGUILayout.PropertyField(_optimization3, false);
                EditorGUILayout.PropertyField(_optimization4, false);
                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(_optimizationVar, false);
                using (new EditorGUI.DisabledScope(!_settings.EnableVariableReduction))
                {
                    EditorGUILayout.PropertyField(_optimizationBR, false);
                    EditorGUILayout.PropertyField(_optimizationSL, false);
                    EditorGUILayout.PropertyField(_optimizationThis, false);
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