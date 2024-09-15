using System;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

#pragma warning disable IDE0029 // Use coalesce expression
#pragma warning disable RCS1084 // Use coalesce expression instead of conditional expression

namespace UdonSharpOptimizer
{
    internal class OptimizerSettings : ScriptableObject
    {
        [Tooltip("Enable or disable the optimizer entirely")]
        public bool EnableOptimizer = true;

        [Tooltip("Targets COPY+JUMP_IF_FALSE")]
        public bool EnableOPT01 = true;
        [Tooltip("Targets EXTERN+COPY")]
        public bool EnableOPT02 = true;
        [Tooltip("Targets Unread COPY (Cow dirty)")]
        public bool EnableOPT03 = true;
        [Tooltip("Performs Tail Call Optimization")]
        public bool EnableOPT04 = true;
        [Tooltip("Reduce amount of temporary variables")]
        public bool EnableVariableReduction = true;
        [Tooltip("Map variables to same variable in different blocks")]
        public bool EnableBlockReduction = true;
        [Tooltip("Map Store+Load variables to same variable")]
        public bool EnableStoreLoad = true;
        [Tooltip("Fix extra __this_ variables")]
        public bool EnableThisBugFix = true;

        private static OptimizerSettings _instance;

        public static OptimizerSettings Instance => _instance != null ? _instance : (_instance = LoadAsset());

        private static OptimizerSettings LoadAsset()
        {
            string path = GetAssetPath();
            OptimizerSettings asset = AssetDatabase.LoadAssetAtPath<OptimizerSettings>(path);

            if (asset == null)
            {
                asset = CreateInstance<OptimizerSettings>();
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
            }

            return asset;
        }

        private static string GetAssetPath([CallerFilePath] string callerFilePath = null)
        {
            // TODO: This cannot be the correct way to do this ...
            string path = Path.GetDirectoryName(callerFilePath);
            path = Path.Combine(path, "OptimizerSettings.asset");
#if NET_UNITY_4_8
            path = Path.GetRelativePath(Path.GetDirectoryName(Application.dataPath), path);
#else
            path = GetRelativePath(Path.GetDirectoryName(Application.dataPath), path);
#endif
            return path.Replace("\\", "/");
        }

#if !NET_UNITY_4_8
        private static string GetRelativePath(string fromPath, string toPath)
        {
            if (!fromPath.EndsWith(Path.DirectorySeparatorChar.ToString()) && !fromPath.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            {
                fromPath += Path.DirectorySeparatorChar;
            }
            Uri fromUri = new Uri(fromPath);
            Uri toUri = new Uri(toPath);
            if (fromUri.Scheme != toUri.Scheme)
            {
                return toPath;
            }
            return Uri.UnescapeDataString(fromUri.MakeRelativeUri(toUri).ToString());
        }
#endif
    }
}