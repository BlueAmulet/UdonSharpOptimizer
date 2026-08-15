/*
 * Unofficial UdonSharp Optimizer
 * Helper to persist statistics across code reload
 * Written by BlueAmulet
 */

using System;
using UnityEditor;

namespace UdonSharpOptimizer
{
    internal static class OptimizerStats
    {
        private const string KeyPrefix = "UdonSharpOptimizer.Stats.";

        public static string KeyFor(string id)
        {
            return KeyPrefix + id;
        }

        public static string KeyFor(Type optimizationType)
        {
            return KeyFor(optimizationType.Name);
        }

        public static int Load(string key)
        {
            return SessionState.GetInt(key, 0);
        }

        public static void Save(string key, int value)
        {
            SessionState.SetInt(key, value);
        }
    }
}
