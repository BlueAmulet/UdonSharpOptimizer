/*
 * Unofficial UdonSharp Optimizer
 * Written by BlueAmulet
 */

using UnityEditor;
using UnityEngine;

namespace UdonSharpOptimizer
{
    // Settings and Statistics window
    internal sealed class OptimizerEditorWindow : EditorWindow
    {
        OptimizerSettings _settings;
        SerializedObject _settingsSO;

        SerializedProperty _optEnabled;
        SerializedProperty _optCopyAndLoad;
        SerializedProperty _optCopyAndTest;
        SerializedProperty _optStoreAndCopy;
        SerializedProperty _optDoubleCopy;
        SerializedProperty _optCleanUnreadCopy;
        SerializedProperty _optDirectJump;
        SerializedProperty _optTCO;
        SerializedProperty _optVariables;
        SerializedProperty _optBlockReduction;
        SerializedProperty _optStoreLoad;
        SerializedProperty _optThis;

        Vector2 _scrollPos;
        bool _statusOpen = true;
        bool _settingsOpen = true;
        bool _buildInfoOpen = true;
        bool _detailedInstrOpen = false;

        [MenuItem("UdonSharpOptimizer/Settings")]
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
            _optDirectJump = _settingsSO.FindProperty(nameof(OptimizerSettings.DirectJump));
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

            GUIStyle richLabel = new GUIStyle(EditorStyles.label);
            richLabel.richText = true;
            GUIStyle boldFoldout = new GUIStyle(EditorStyles.foldout);
            boldFoldout.fontStyle = FontStyle.Bold;

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUIStyle.none, GUI.skin.verticalScrollbar);

            // Optimizer status
            _statusOpen = EditorGUILayout.Foldout(_statusOpen, "Status:", boldFoldout);
            if (_statusOpen)
            {
                EditorGUI.indentLevel++;
                AlignedText("Version:", Optimizer.Version, richLabel);
                string statusText = OptimizerInject.PatchSuccess
                    ? (_settings.EnableOptimizer ? "lime>Activated" : ">Inactive")
                    : "orange><b>Failed to inject</b>";
                AlignedText("Optimizer:", $"<color={statusText}</color>", richLabel);
                int patchFailures = OptimizerInject.PatchFailures;
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.PrefixLabel("Patches:");
                    EditorGUILayout.LabelField(patchFailures == 0
                        ? $"<color=lime>{patchFailures} patch failures</color>"
                        : $"<color=orange><b>{patchFailures} patch failures</b></color>", richLabel);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space();

            // Settings
            _settingsOpen = EditorGUILayout.Foldout(_settingsOpen, "Settings:", boldFoldout);
            if (_settingsOpen)
            {
                EditorGUI.indentLevel++;
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
                    EditorGUILayout.PropertyField(_optDirectJump, false);
                    EditorGUILayout.PropertyField(_optTCO, new GUIContent("Optimize Tail Calls"), false);
                    EditorGUILayout.Space();
                    EditorGUILayout.PropertyField(_optVariables, new GUIContent("Reduce Variables"), false);
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
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space();

            // Last Build information
            _buildInfoOpen = EditorGUILayout.Foldout(_buildInfoOpen, "Last Build:", boldFoldout);
            if (_buildInfoOpen)
            {
                EditorGUI.indentLevel++;
                AlignedText("Instructions:", $"{Optimizer.RemovedInstructions} removed", EditorStyles.label);
                AlignedText("Variables:", $"{Optimizer.RemovedVariables} removed", EditorStyles.label);
                AlignedText("Extra __this:", $"{Optimizer.RemovedThisTotal} removed", EditorStyles.label);
                _detailedInstrOpen = EditorGUILayout.Foldout(_detailedInstrOpen, "Details:", boldFoldout);
                if (_detailedInstrOpen)
                {
                    Optimizer.OnGUI();
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndScrollView();
        }

        internal static void AlignedText(string prefix, string text, GUIStyle style)
        {
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.PrefixLabel(prefix);
                EditorGUILayout.LabelField(text, style);
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}