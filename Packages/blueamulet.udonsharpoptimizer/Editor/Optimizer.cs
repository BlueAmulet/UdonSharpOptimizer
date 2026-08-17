/*
 * Unofficial UdonSharp Optimizer
 * Written by BlueAmulet
 */

using System;
using System.IO;
using System.Threading;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Assembly.Instructions;
using UdonSharp.Compiler.Emit;
using UdonSharpOptimizer.Optimizations;
using UdonSharpOptimizer.Passes;
using UnityEngine;

namespace UdonSharpOptimizer
{
    // Handles running all optimization passes
    internal static class Optimizer
    {
        public static readonly string Version = "1.1.1";

        private static readonly OptimizerSettings Settings = OptimizerSettings.Instance;

        // Keys for persisting statistics
        private static readonly string InstructionsStatsKey = OptimizerStats.KeyFor("Instructions");
        private static readonly string VariablesStatsKey = OptimizerStats.KeyFor("Variables");
        private static readonly string ThisTotalStatsKey = OptimizerStats.KeyFor("ThisTotal");

        // Various statistics
        private static int RemovedInstructionsCounter = OptimizerStats.Load(InstructionsStatsKey);
        private static int RemovedVariablesCounter = OptimizerStats.Load(VariablesStatsKey);
        private static int RemovedThisTotalCounter = OptimizerStats.Load(ThisTotalStatsKey);

        // For Settings panel
        public static int RemovedInstructions => RemovedInstructionsCounter;
        public static int RemovedVariables => RemovedVariablesCounter;
        public static int RemovedThisTotal => RemovedThisTotalCounter;

        // Optimizations
        private static readonly IInstructionPass[] Optimizations = {
            new OPTCopyLoad(),
            new OPTCopyTest(),
            new OPTStoreCopy(),
            new OPTDoubleCopy(),
            new OPTUnreadCopy(),
            new OPTDirectJump(),
            new OPTTailCall(),
        };

        private static readonly IOptimizerPass[] Passes = {
            new InitializeContextPass(),
            new PeepholeOptimizationPass(Optimizations),
            new VariableReductionPass(),
            new FinalizePass(),
        };

        // Required to remove beforefieldinit flag and force consistent static initialization
        static Optimizer()
        {
        }

        internal static void ResetGlobalCounters()
        {
            RemovedInstructionsCounter = 0;
            RemovedVariablesCounter = 0;
            RemovedThisTotalCounter = 0;
            foreach (IInstructionPass optimization in Optimizations)
            {
                optimization.ResetStats();
            }
        }

        internal static void AddGlobalCounters(OptimizerContext context)
        {
            Interlocked.Add(ref RemovedInstructionsCounter, context.RemovedInstrs);
            Interlocked.Add(ref RemovedVariablesCounter, context.RemovedValues);
            Interlocked.Add(ref RemovedThisTotalCounter, context.RemovedThis);
        }

        internal static void SaveStats()
        {
            OptimizerStats.Save(InstructionsStatsKey, RemovedInstructionsCounter);
            OptimizerStats.Save(VariablesStatsKey, RemovedVariablesCounter);
            OptimizerStats.Save(ThisTotalStatsKey, RemovedThisTotalCounter);
            foreach (IInstructionPass optimization in Optimizations)
            {
                optimization.SaveStats();
            }
        }

        internal static void OnGUI()
        {
            foreach (IInstructionPass optimization in Optimizations)
            {
                optimization.OnGUI();
            }
        }

        internal static void OptimizeProgram(EmitContext moduleEmitContext)
        {
            if (!Settings.EnableOptimizer)
            {
                return;
            }

            OptimizerContext context = new OptimizerContext(moduleEmitContext);
            try
            {
                foreach (IOptimizerPass pass in Passes)
                {
                    pass.Execute(context);
                }
            }
            catch (OptimizerAbortException ex)
            {
                Debug.LogError($"[Optimizer] Aborted: {ex.Message}");
            }
        }

        // Utility methods
        internal static bool IsExternWrite(AssemblyInstruction instr)
        {
            if (instr is ExternInstruction extInst)
            {
                return !extInst.Extern.ExternSignature.EndsWith("__SystemVoid", StringComparison.Ordinal);
            }
            return instr is ExternGetInstruction;
        }

        internal static bool IsPrivate(Value value)
        {
            return value.IsInternal || value.IsLocal;
        }
    }
}