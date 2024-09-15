/*
 * Unoffical UdonSharp Optimizer
 * Integrates the Optimizer with UdonSharp
 * Written by BlueAmulet
 */

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UdonSharp.Compiler;
using UnityEditor;
using UnityEngine;

namespace UdonSharpOptimizer
{
    [InitializeOnLoad]
    public static class OptimizerInject
    {
        private const string HARMONY_ID = "BlueAmulet.USOptimizer.Injector";
        private static Harmony harmony;
        private static readonly MethodInfo optimizerInject = AccessTools.Method(typeof(Optimizer), nameof(Optimizer.OptimizeProgram));

        static OptimizerInject()
        {
            harmony = new Harmony(HARMONY_ID);
            harmony.UnpatchAll(HARMONY_ID);
            MethodInfo target = AccessTools.Method(typeof(UdonSharpCompilerV1), "EmitAllPrograms");
            MethodInfo transpiler = AccessTools.Method(typeof(OptimizerInject), nameof(TranspilerEmit));
            harmony.Patch(target, null, null, new HarmonyMethod(transpiler));
        }

        private static IEnumerable<CodeInstruction> TranspilerEmit(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> instrs = new List<CodeInstruction>(instructions);
            bool ldftn = false;
            foreach (CodeInstruction instr in instrs)
            {
                // Locate the internal lambda function
                if (instr.opcode == OpCodes.Ldftn)
                {
                    MethodInfo transpiler = AccessTools.Method(typeof(OptimizerInject), nameof(TranspilerDump));
                    harmony.Patch((MethodBase)instr.operand, null, null, new HarmonyMethod(transpiler));
                    ldftn = true;
                }
            }
            if (!ldftn)
            {
                Debug.LogError("[Optimizer] Failed to locate internal parallel function");
            }
            return instrs;
        }

        private static IEnumerable<CodeInstruction> TranspilerDump(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> instrs = new List<CodeInstruction>(instructions);
            bool patched = false;
            bool already = false;
            bool foundEmitContext = false;
            int emitContextLocal = -1;
            for (int i = 0; i < instrs.Count; i++)
            {
                CodeInstruction instr = instrs[i];
                if (instr.opcode == OpCodes.Newobj && instr.ToString() == "newobj System.Void UdonSharp.Compiler.Emit.EmitContext::.ctor(UdonSharp.Compiler.Assembly.AssemblyModule module, Microsoft.CodeAnalysis.ITypeSymbol emitType)")
                {
                    CodeInstruction next = instrs[i + 1];
                    if (next.IsStloc())
                    {
                        foundEmitContext = true;
                        emitContextLocal = LocalIndex(next);
                    }
                    else
                    {
                        Debug.LogError("[Optimizer] Expected stloc after newobj EmitContext");
                    }
                }
                if (instr.opcode == OpCodes.Stfld && instr.operand.ToString() == "System.Collections.Generic.Dictionary`2[System.String,UdonSharp.Compiler.FieldDefinition] fieldDefinitions")
                {
                    if (foundEmitContext)
                    {
                        if (instrs[i + 1].opcode == OpCodes.Ldarg_0)
                        {
                            instrs.InsertRange(i + 1, new List<CodeInstruction>() {
                                new CodeInstruction(OpCodes.Ldloc, emitContextLocal),
                                new CodeInstruction(OpCodes.Call, optimizerInject)
                            });
                            patched = true;
                        }
                        else if (instrs[i + 1].opcode == OpCodes.Ldloc && instrs[i + 2].opcode == OpCodes.Call && (MethodInfo)instrs[i + 2].operand == optimizerInject)
                        {
                            already = true;
                        }
                    }
                    else
                    {
                        Debug.LogError("[Optimizer] Found injection point but missing EmitContext");
                    }
                    break;
                }
            }

            if (patched)
            {
                Debug.Log("[Optimizer] Activated");
            }
            else if (!already)
            {
                Debug.LogError("[Optimizer] Failed to inject");
            }
            return instrs;
        }

        public static int LocalIndex(CodeInstruction code)
        {
            if (code.opcode == OpCodes.Ldloc_0 || code.opcode == OpCodes.Stloc_0) return 0;
            else if (code.opcode == OpCodes.Ldloc_1 || code.opcode == OpCodes.Stloc_1) return 1;
            else if (code.opcode == OpCodes.Ldloc_2 || code.opcode == OpCodes.Stloc_2) return 2;
            else if (code.opcode == OpCodes.Ldloc_3 || code.opcode == OpCodes.Stloc_3) return 3;
            else if (code.opcode == OpCodes.Ldloc_S || code.opcode == OpCodes.Ldloc) return Convert.ToInt32(code.operand);
            else if (code.opcode == OpCodes.Stloc_S || code.opcode == OpCodes.Stloc) return Convert.ToInt32(code.operand);
            else if (code.opcode == OpCodes.Ldloca_S || code.opcode == OpCodes.Ldloca) return Convert.ToInt32(code.operand);
            else throw new ArgumentException("Instruction is not a load or store", nameof(code));
        }
    }
}