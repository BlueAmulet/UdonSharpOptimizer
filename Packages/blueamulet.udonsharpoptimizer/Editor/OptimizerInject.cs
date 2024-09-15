/*
 * Unofficial UdonSharp Optimizer
 * Integrates the Optimizer with UdonSharp
 * Written by BlueAmulet
 */

using HarmonyLib;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharp.Compiler.Binder;
using UdonSharp.Compiler.Emit;
using UdonSharp.Compiler.Symbols;
using UnityEditor;
using UnityEngine;

#pragma warning disable IDE0090 // Use 'new(...)'

namespace UdonSharpOptimizer
{
    [InitializeOnLoad]
    internal static class OptimizerInject
    {
        private const string HARMONY_ID = "BlueAmulet.USOptimizer.UdonSharpPatch";
        private static readonly Harmony Harmony = new Harmony(HARMONY_ID);

        private static readonly MethodInfo optimizerInject = AccessTools.Method(typeof(OptimizerInject), nameof(OptimizeHook));

        private static bool _patchSuccess;
        public static bool PatchSuccess => _patchSuccess;

        private static int _patchFailures;
        public static int PatchFailures => _patchFailures;

        static OptimizerInject()
        {
            // Load settings here so we don't try to do it off the main thread in Optimizer
            _ = OptimizerSettings.Instance;
            AssemblyReloadEvents.afterAssemblyReload += RunPostAssemblyBuildRefresh;
        }

        /* UdonSharp Hooks */
        private static void RunPostAssemblyBuildRefresh()
        {
            using (new UdonSharpUtils.UdonSharpAssemblyLoadStripScope())
            {
                Harmony.UnpatchAll(HARMONY_ID);
                _patchFailures = 0;

                // Inject Optimizer into UdonSharp
                MethodInfo target = AccessTools.Method(typeof(UdonSharpCompilerV1), "EmitAllPrograms");
                MethodInfo transpiler = AccessTools.Method(typeof(OptimizerInject), nameof(TranspilerEmit));
                Harmony.Patch(target, null, null, new HarmonyMethod(transpiler));

                // Add debug string to return address values for identification
                MethodInfo emitReturnAddr = AccessTools.Method(typeof(BoundUserMethodInvocationExpression), nameof(BoundUserMethodInvocationExpression.EmitValue));
                MethodInfo retAddrLabel = AccessTools.Method(typeof(OptimizerInject), nameof(ReturnValueTranspiler));
                Harmony.Patch(emitReturnAddr, null, null, new HarmonyMethod(retAddrLabel));

                // Add debug string to switch table values for identification
                MethodInfo emitJumpTable = AccessTools.Method(typeof(BoundSwitchStatement), "EmitJumpTableSwitchStatement");
                MethodInfo jumpTableLabel = AccessTools.Method(typeof(OptimizerInject), nameof(SwitchTableTranspiler));
                Harmony.Patch(emitJumpTable, null, null, new HarmonyMethod(jumpTableLabel));

                // Fix UdonSharp creating unnecessary additional variables for __this
                // Done in post instead for statistics
                /*
                MethodInfo udonThis = AccessTools.Method(typeof(ValueTable), nameof(ValueTable.GetUdonThisValue));
                MethodInfo udonThisFix = AccessTools.Method(typeof(OptimizerInject), nameof(UdonThisFix));
                harmony.Patch(udonThis, new HarmonyMethod(udonThisFix));
                //*/

                // Add hook to compiler to reset optimizer's global counters
                MethodInfo emitProgram = AccessTools.Method(typeof(UdonSharpCompilerV1), "EmitAllPrograms");
                MethodInfo optPre = AccessTools.Method(typeof(Optimizer), nameof(Optimizer.ResetGlobalCounters));
                MethodInfo optPost = AccessTools.Method(typeof(Optimizer), nameof(Optimizer.LogGlobalCounters));
                Harmony.Patch(emitProgram, new HarmonyMethod(optPre), new HarmonyMethod(optPost));
            }
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
                    Harmony.Patch((MethodBase)instr.operand, null, null, new HarmonyMethod(transpiler));
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

            _patchSuccess = patched || already;
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

        private static void OptimizeHook(EmitContext moduleEmitContext)
        {
            Optimizer optimizer = new Optimizer(moduleEmitContext);
            optimizer.OptimizeProgram();
        }

        private static IEnumerable<CodeInstruction> ReturnValueTranspiler(IEnumerable<CodeInstruction> instrEnumerator)
        {
            List<CodeInstruction> instr = new List<CodeInstruction>(instrEnumerator);

            // Locate CreateGlobalInternalValue call
            bool patched = false;
            for (int i = 0; i < instr.Count; i++)
            {
                CodeInstruction inst = instr[i];

                if (inst.opcode == OpCodes.Ldloc_0 && i < instr.Count - 4)
                {
                    CodeInstruction inst2 = instr[i + 1];
                    CodeInstruction inst3 = instr[i + 2];
                    CodeInstruction inst4 = instr[i + 3];
                    CodeInstruction inst5 = instr[i + 4];
                    if ((inst2.opcode == OpCodes.Ldfld) &&
                        (inst3.opcode == OpCodes.Ldc_I4_S && (sbyte)inst3.operand == 14) &&
                        (inst4.opcode == OpCodes.Callvirt && (MethodInfo)inst4.operand == AccessTools.Method(typeof(AbstractPhaseContext), nameof(AbstractPhaseContext.GetTypeSymbol), new Type[] { typeof(SpecialType) })) &&
                        (inst5.opcode == OpCodes.Callvirt && (MethodInfo)inst5.operand == AccessTools.Method(typeof(EmitContext), nameof(EmitContext.CreateGlobalInternalValue))))
                    {
                        // Add get_TopTable()
                        instr.Insert(i, new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(EmitContext), "get_TopTable")));

                        // Inject debug string for easy optimizer analysis
                        instr.InsertRange(instr.IndexOf(inst5), new List<CodeInstruction>()
                        {
                            new CodeInstruction(OpCodes.Ldstr, "RetAddress"),
                            new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(ValueTable), "CreateGlobalInternalValue")),
                        });
                        instr.Remove(inst5);

                        patched = true;
                        break;
                    }
                }
            }

            if (!patched)
            {
                Debug.LogWarning("[Optimizer] Failed to add debug string to return value");
                _patchFailures++;
                return instrEnumerator;
            }

            return instr;
        }

        private static IEnumerable<CodeInstruction> SwitchTableTranspiler(IEnumerable<CodeInstruction> instrEnumerator)
        {
            List<CodeInstruction> instr = new List<CodeInstruction>(instrEnumerator);

            // Locate CreateGlobalInternalValue call
            bool patched = false;
            for (int i = 0; i < instr.Count; i++)
            {
                CodeInstruction inst = instr[i];

                if (inst.opcode == OpCodes.Ldarg_1 && i < instr.Count - 5)
                {
                    CodeInstruction inst2 = instr[i + 1];
                    CodeInstruction inst3 = instr[i + 2];
                    CodeInstruction inst4 = instr[i + 3];
                    CodeInstruction inst5 = instr[i + 4];
                    CodeInstruction inst6 = instr[i + 5];
                    if ((inst2.opcode == OpCodes.Ldc_I4_S && (sbyte)inst2.operand == 14) &&
                        (inst3.opcode == OpCodes.Callvirt && ((MethodInfo)inst3.operand).Name == nameof(AbstractPhaseContext.GetTypeSymbol)) &&
                        (inst4.opcode == OpCodes.Ldarg_1) &&
                        (inst5.opcode == OpCodes.Callvirt && (MethodInfo)inst5.operand == AccessTools.Method(typeof(TypeSymbol), nameof(TypeSymbol.MakeArrayType), new Type[] { typeof(AbstractPhaseContext) })) &&
                        (inst6.opcode == OpCodes.Callvirt && (MethodInfo)inst6.operand == AccessTools.Method(typeof(EmitContext), nameof(EmitContext.CreateGlobalInternalValue))))
                    {
                        // Add get_TopTable()
                        instr.Insert(i, new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(EmitContext), "get_TopTable")));

                        // Inject debug string for easy optimizer analysis
                        instr.InsertRange(instr.IndexOf(inst6), new List<CodeInstruction>()
                        {
                            new CodeInstruction(OpCodes.Ldstr, "SwitchTable"),
                            new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(ValueTable), "CreateGlobalInternalValue")),
                        });
                        instr.Remove(inst6);

                        patched = true;
                        break;
                    }
                }
            }

            if (!patched)
            {
                _patchFailures++;
                Debug.LogWarning("[Optimizer] Failed to add debug string to switch jump table");
                return instrEnumerator;
            }

            return instr;
        }

        /*
        private static bool UdonThisFix(ref ValueTable __instance, ref Value __result, ref TypeSymbol type)
        {
            Type systemType = type.UdonType.SystemType;
            foreach (Value globalValue in __instance.GlobalTable.Values)
            {
                if ((globalValue.Flags & Value.ValueFlags.UdonThis) != 0 && globalValue.UdonType.SystemType == systemType)
                {
                    __result = globalValue;
                    if (globalValue.UdonType != type)
                    {
                        Interlocked.Increment(ref Optimizer.removedThisTotal);
                    }
                    return false;
                }
            }
            return true;
        }
        //*/

        private static int LocalIndex(CodeInstruction code)
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