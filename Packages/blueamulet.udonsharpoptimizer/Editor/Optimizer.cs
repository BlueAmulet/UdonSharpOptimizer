/*
 * Unoffical UdonSharp Optimizer
 * The Optimizer.
 * Version 1.0.8
 * Written by BlueAmulet
 */

using HarmonyLib;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Assembly.Instructions;
using UdonSharp.Compiler.Binder;
using UdonSharp.Compiler.Emit;
using UdonSharp.Compiler.Symbols;
using UnityEditor;
using UnityEngine;

#pragma warning disable IDE0090 // Use 'new(...)'
#pragma warning disable IDE0063 // Use simple 'using' statement
#pragma warning disable IDE0028 // Simplify collection initialization

namespace UdonSharpOptimizer
{
    [InitializeOnLoad]
    internal class Optimizer
    {
        private const string HARMONY_ID = "BlueAmulet.USOptimizer.UdonSharpPatch";
        private static readonly OptimizerSettings settings = OptimizerSettings.Instance;

        private static readonly AccessTools.FieldRef<object, List<AssemblyInstruction>> _instructions = AccessTools.FieldRefAccess<List<AssemblyInstruction>>(typeof(AssemblyModule), "_instructions");
        private static readonly AccessTools.FieldRef<object, List<MethodDebugInfo>> _methodDebugInfos = AccessTools.FieldRefAccess<List<MethodDebugInfo>>(typeof(AssemblyDebugInfo), "_methodDebugInfos");
        private static readonly AccessTools.FieldRef<object, ValueTable> _parentTable = AccessTools.FieldRefAccess<ValueTable>(typeof(Value), "_parentTable");

        private static int patchFailures;
        public static int PatchFailures { get => patchFailures; }

        // Various statistics
        private static int removedInstructions;
        private static int removedVariables;
        private static int removedThisTotal;

        // For Settings panel
        public static int RemovedInstructions { get => removedInstructions; }
        public static int RemovedVariables { get => removedVariables; }
        public static int RemovedThisTotal { get => removedThisTotal; }

        static Optimizer()
        {
            AssemblyReloadEvents.afterAssemblyReload += RunPostAssemblyBuildRefresh;
        }

        /* UdonSharp Hooks */
        private static void RunPostAssemblyBuildRefresh()
        {
            Harmony harmony = new Harmony(HARMONY_ID);

            using (new UdonSharpUtils.UdonSharpAssemblyLoadStripScope())
            {
                harmony.UnpatchAll(HARMONY_ID);
                patchFailures = 0;

                // Add debug string to return address values for identification
                MethodInfo target = AccessTools.Method(typeof(BoundUserMethodInvocationExpression), nameof(BoundUserMethodInvocationExpression.EmitValue));
                MethodInfo transpiler = AccessTools.Method(typeof(Optimizer), nameof(Optimizer.ReturnValueTranspiler));
                harmony.Patch(target, null, null, new HarmonyMethod(transpiler));

                // Add debug string to switch table values for identification
                MethodInfo target2 = AccessTools.Method(typeof(BoundSwitchStatement), "EmitJumpTableSwitchStatement");
                MethodInfo transpiler2 = AccessTools.Method(typeof(Optimizer), nameof(Optimizer.SwitchTableTranspiler));
                harmony.Patch(target2, null, null, new HarmonyMethod(transpiler2));

                // Fix UdonSharp creating unnecessary additional variables for __this
                MethodInfo udonThis = AccessTools.Method(typeof(ValueTable), nameof(ValueTable.GetUdonThisValue));
                MethodInfo udonThisFix = AccessTools.Method(typeof(Optimizer), nameof(Optimizer.UdonThisFix));
                //harmony.Patch(udonThis, new HarmonyMethod(udonThisFix));

                // Add hook to compiler to reset instruction counter
                MethodInfo emitProgram = AccessTools.Method(typeof(UdonSharpCompilerV1), "EmitAllPrograms");
                MethodInfo optPre = AccessTools.Method(typeof(Optimizer), nameof(Optimizer.EmitAllProgramsPrefix));
                MethodInfo optPost = AccessTools.Method(typeof(Optimizer), nameof(Optimizer.EmitAllProgramsPostfix));
                harmony.Patch(emitProgram, new HarmonyMethod(optPre), new HarmonyMethod(optPost));
            }
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
                patchFailures++;
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
                patchFailures++;
                Debug.LogWarning("[Optimizer] Failed to add debug string to switch jump table");
                return instrEnumerator;
            }

            return instr;
        }

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
                        Interlocked.Increment(ref removedThisTotal);
                    }
                    return false;
                }
            }
            return true;
        }

        private static void EmitAllProgramsPrefix()
        {
            ResetRemoved();
        }

        private static void EmitAllProgramsPostfix()
        {
            Debug.Log($"[Optimizer] Removed {removedInstructions} instructions, {removedVariables} variables, and {removedThisTotal} extra __this total");
        }

        /* The Optimizer */
        internal static void ResetRemoved()
        {
            removedInstructions = 0;
            removedVariables = 0;
            removedThisTotal = 0;
        }

        private static Comment CopyComment(string code, CopyInstruction cInst)
        {
            return new Comment($"{code}: Removed {cInst.SourceValue.UniqueID} => {cInst.TargetValue.UniqueID} copy");
        }

        private static bool IsExternWrite(AssemblyInstruction inst)
        {
            if (inst is ExternInstruction extInst)
            {
                return !extInst.Extern.ExternSignature.EndsWith("__SystemVoid");
            }
            return inst is ExternGetInstruction;
        }

        private static bool IsPrivate(Value value)
        {
            return value.IsInternal || value.IsLocal;
        }

        private static bool HasJump(List<AssemblyInstruction> instr, HashSet<AssemblyInstruction> hasJump, int min, int max)
        {
            for (int i = min; i <= max; i++)
            {
                if (hasJump.Contains(instr[i]))
                {
                    return true;
                }
            }
            return false;
        }

        // Full code scan, except optimizable patterns are ignored
        private static bool ReadScan(List<AssemblyInstruction> instr, Func<int, bool> ignore, Value value, HashSet<AssemblyInstruction> hasJump)
        {
            HashSet<int> ignoreOpt = new HashSet<int>();
            for (int i = 0; i < instr.Count; i++)
            {
                if (!ignore(i) && !ignoreOpt.Contains(i))
                {
                    // The last instruction of a method should be RetInstruction, so the missing bound checks should be safe
                    if (instr[i] is PushInstruction pInst && pInst.PushValue.UniqueID == value.UniqueID)
                    {
                        // Ignore pushes followed by a extern that returns a value
                        if (!IsExternWrite(instr[i + 1]))
                        {
                            return true;
                        }
                        else if (instr[i + 2] is CopyInstruction cInst && cInst.SourceValue.UniqueID == value.UniqueID && !HasJump(instr, hasJump, i + 1, i + 2))
                        {
                            // This should be safe, the variable was JUST overridden, so the read isn't true
                            // Will be cleaned up by OPT02
                            ignoreOpt.Add(i + 2);
                        }
                    }
                    else if (instr[i] is CopyInstruction cInst && cInst.SourceValue.UniqueID == value.UniqueID)
                    {
                        return true;
                    }
                    else if (instr[i] is JumpIfFalseInstruction jifInst && jifInst.ConditionValue.UniqueID == value.UniqueID)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        internal static void OptimizeProgram(EmitContext moduleEmitContext)
        {
            if (!settings.EnableOptimizer)
            {
                return;
            }

            AssemblyModule assemblyModule = moduleEmitContext.Module;
            List<AssemblyInstruction> instrs = new List<AssemblyInstruction>();
            List<JumpLabel> jumpLabels = new List<JumpLabel>();
            List<Value> addrValues = new List<Value>();
            List<Value> switchTables = new List<Value>();
            HashSet<AssemblyInstruction> hasJump = new HashSet<AssemblyInstruction>();

            // Copy instructions out of module
            for (int i = 0; i < assemblyModule.InstructionCount; i++)
            {
                AssemblyInstruction inst = assemblyModule[i];
                instrs.Add(inst);
                if (inst is JumpInstruction jInst && !jumpLabels.Contains(jInst.JumpTarget))
                {
                    jumpLabels.Add(jInst.JumpTarget);
                }
                else if (inst is JumpIfFalseInstruction jifInst && !jumpLabels.Contains(jifInst.JumpTarget))
                {
                    jumpLabels.Add(jifInst.JumpTarget);
                }
                else if (inst is PushInstruction pInst && pInst.PushValue.Flags == Value.ValueFlags.InternalGlobal)
                {
                    if (pInst.PushValue.DefaultValue is uint && pInst.PushValue.UniqueID.StartsWith("__gintnl_RetAddress_"))
                    {
                        addrValues.Add(pInst.PushValue);
                    }
                    else if (pInst.PushValue.DefaultValue is uint[] && pInst.PushValue.UniqueID.StartsWith("__gintnl_SwitchTable_"))
                    {
                        switchTables.Add(pInst.PushValue);
                    }
                }
            }

            // Make a set of instructions that can be jumped to
            jumpLabels.Sort((a, b) => a.Address.CompareTo(b.Address));
            List<JumpLabel>.Enumerator jumpEnumerator = jumpLabels.GetEnumerator();
            if (jumpEnumerator.MoveNext())
            {
                foreach (AssemblyInstruction inst in instrs)
                {
                    if (inst.Size != 0 && inst.InstructionAddress == jumpEnumerator.Current.Address)
                    {
                        hasJump.Add(inst);
                        while (inst.InstructionAddress == jumpEnumerator.Current.Address)
                        {
                            if (!jumpEnumerator.MoveNext())
                            {
                                break;
                            }
                        }
                        if (jumpEnumerator.Current == null)
                        {
                            break;
                        }
                    }
                    if (inst.InstructionAddress > jumpEnumerator.Current.Address)
                    {
                        // Shouldn't happen but just in case
                        Debug.LogError("[Optimizer] Jump Target Set Desync");
                        return;
                    }
                }
            }

            // The actual optimizations
            int removedInsts = 0;
            for (int i = 0; i < instrs.Count; i++)
            {
                // Remove Copy: Copy + JumpIf
                if (settings.EnableOPT01)
                {
                    if (instrs[i] is CopyInstruction cInst && i < instrs.Count - 1 && instrs[i + 1] is JumpIfFalseInstruction jifInst)
                    {
                        if (IsPrivate(cInst.TargetValue) && cInst.TargetValue.UniqueID == jifInst.ConditionValue.UniqueID && !hasJump.Contains(jifInst) && !ReadScan(instrs, n => n == i + 1, cInst.TargetValue, hasJump))
                        {
                            instrs[i] = TransferInstr(CopyComment("OPT01", cInst), cInst, hasJump);
                            instrs[i + 1] = TransferInstr(new JumpIfFalseInstruction(jifInst.JumpTarget, cInst.SourceValue), jifInst, hasJump);
                            removedInsts += 3; // PUSH, PUSH, COPY
                            //Debug.Log($"[Optimizer] OPT1: Dropped copy to {cInst.TargetValue.UniqueID}");
                        }
                    }
                }

                // Remove Copy: Extern + Copy
                if (settings.EnableOPT02)
                {
                    if (instrs[i] is PushInstruction pInst && i < instrs.Count - 2 && IsExternWrite(instrs[i + 1]) && instrs[i + 2] is CopyInstruction cInst)
                    {
                        if (IsPrivate(pInst.PushValue) && pInst.PushValue.UniqueID == cInst.SourceValue.UniqueID && !HasJump(instrs, hasJump, i + 1, i + 2) && !ReadScan(instrs, n => n == i || n == i + 2, pInst.PushValue, hasJump))
                        {
                            instrs[i] = TransferInstr(new PushInstruction(cInst.TargetValue), pInst, hasJump);
                            instrs[i + 2] = TransferInstr(CopyComment("OPT02", cInst), cInst, hasJump);
                            removedInsts += 3; // PUSH, PUSH, COPY
                            //Debug.Log($"[Optimizer] OPT2: Dropped copy to {cInst.TargetValue.UniqueID}");
                        }
                    }
                }

                // Remove Copy: Unread target (Cleans up Cow dirty)
                if (settings.EnableOPT03)
                {
                    if (instrs[i] is CopyInstruction cInst)
                    {
                        if (IsPrivate(cInst.TargetValue) && !ReadScan(instrs, _ => false, cInst.TargetValue, hasJump))
                        {
                            instrs[i] = TransferInstr(CopyComment("OPT03", cInst), cInst, hasJump);
                            removedInsts += 3; // PUSH, PUSH, COPY
                            //Debug.Log($"[Optimizer] OPT3: Dropped copy to {cInst.TargetValue.UniqueID}");
                        }
                    }
                }
            }
            //Debug.Log($"Removed {removedInsts} instructions");

            // Pass 2: Tail call optimization
            if (settings.EnableOPT04)
            {
                for (int i = 0; i < instrs.Count - 1; i++)
                {
                    // TODO: Properly verify this jump is to a method
                    if (instrs[i] is JumpInstruction jInst && instrs[i + 1] is RetInstruction rInst && !hasJump.Contains(rInst) && instrs[i - 1] is Comment cInst && cInst.Comment.StartsWith("Calling "))
                    {
                        // Locate the corresponding push above
                        int pushIdx = -1;
                        for (int j = i - 1; j >= 0; j--)
                        {
                            if (instrs[j] is PushInstruction pInst && pInst.PushValue.Flags == Value.ValueFlags.InternalGlobal && pInst.PushValue.DefaultValue is uint val && pInst.PushValue.UniqueID.StartsWith("__gintnl_RetAddress_") && val == rInst.InstructionAddress)
                            {
                                pushIdx = j;
                                break;
                            }
                            else if (hasJump.Contains(instrs[j]))
                            {
                                break;
                            }
                        }
                        if (pushIdx != -1)
                        {
                            PushInstruction pInst = (PushInstruction)instrs[pushIdx];
                            instrs[pushIdx] = TransferInstr(new Comment($"OPT04: Tail call optimization, removed PUSH {pInst.PushValue.UniqueID}"), instrs[pushIdx], hasJump);
                            instrs[i + 1] = TransferInstr(new Comment($"OPT04: Tail call optimization, removed RET {rInst.RetValRef.UniqueID}"), rInst, hasJump);
                            removedInsts += 4; // PUSH & PUSH + COPY + JUMP_INDIRECT
                        }
                    }
                }
            }

            // Pass 3: Attempt to reduce the number of temporary variables
            int removedValues = 0;
            int removedThis = 0;
            Dictionary<string, Value> tempTable = new Dictionary<string, Value>();
            if (settings.EnableVariableReduction)
            {
                HashSet<Value> notSkippable = new HashSet<Value>();
                HashSet<CopyInstruction> ignoreCopyRead = new HashSet<CopyInstruction>();
                for (int i = 0; i < instrs.Count; i++)
                {
                    AssemblyInstruction instr = instrs[i];
                    if (instr is SyncTag sInst)
                    {
                        notSkippable.Add(sInst.SyncedValue);
                    }
                    else if (instr is PushInstruction pInst)
                    {
                        // Check for extern write + read
                        if (!IsExternWrite(instrs[i + 1]) || hasJump.Contains(instrs[i + 1]))
                        {
                            notSkippable.Add(pInst.PushValue);
                        }
                        else if (instrs[i + 2] is PushInstruction pInst2)
                        {
                            if (pInst.PushValue == pInst2.PushValue && !hasJump.Contains(pInst2))
                            {
                                // Skip it
                                i += 2;
                            }
                            else
                            {
                                notSkippable.Add(pInst.PushValue);
                            }
                        }
                        else if (instrs[i + 2] is CopyInstruction cInst)
                        {
                            if (pInst.PushValue == cInst.SourceValue && !hasJump.Contains(cInst))
                            {
                                // Skip extern but ignore copy's read next loop
                                i++;
                                ignoreCopyRead.Add(cInst);
                            }
                            else
                            {
                                notSkippable.Add(pInst.PushValue);
                            }
                        }
                        else
                        {
                            notSkippable.Add(pInst.PushValue);
                        }
                    }
                    else if (instr is CopyInstruction cInst)
                    {
                        if (!ignoreCopyRead.Contains(cInst))
                        {
                            notSkippable.Add(cInst.SourceValue);
                        }
                        if (instrs[i + 1] is PushInstruction pInst2)
                        {
                            if (cInst.TargetValue == pInst2.PushValue && !hasJump.Contains(pInst2))
                            {
                                // Skip
                                i++;
                            }
                            else
                            {
                                notSkippable.Add(cInst.TargetValue);
                            }
                        }
                        else if (instrs[i + 1] is CopyInstruction cInst2)
                        {
                            if (cInst.TargetValue == cInst2.SourceValue && !hasJump.Contains(cInst2))
                            {
                                // Skip the read of the next copy instruction
                                ignoreCopyRead.Add(cInst2);
                            }
                            else
                            {
                                notSkippable.Add(cInst.TargetValue);
                            }
                        }
                        else
                        {
                            notSkippable.Add(cInst.TargetValue);
                        }
                    }
                    else if (instr is JumpIfFalseInstruction jifInst)
                    {
                        notSkippable.Add(jifInst.ConditionValue);
                    }
                    else if (instr is JumpIndirectInstruction jiInst)
                    {
                        notSkippable.Add(jiInst.JumpTargetValue);
                    }
                    else if (instr is RetInstruction rInst)
                    {
                        notSkippable.Add(rInst.RetValRef);
                    }
                }

                // TODO: What table is this stuff under?
                HashSet<ValueTable> tables = new HashSet<ValueTable>();
                foreach (Value value in notSkippable)
                {
                    tables.Add(_parentTable(value));
                }
                tables.Add(assemblyModule.RootTable);

                // Remove all values that can be reduced to a single temporary
                Dictionary<string, Value> rootThis = null;
                if (settings.EnableThisBugFix)
                {
                    rootThis = new Dictionary<string, Value>();
                }
                foreach (ValueTable table in tables)
                {
                    List<Value> values = table.Values;
                    foreach (Value value in values.ToArray())
                    {
                        if (settings.EnableThisBugFix && (value.Flags & Value.ValueFlags.UdonThis) != 0)
                        {
                            if (value.UniqueID.EndsWith("_0"))
                            {
                                rootThis[value.UdonType.ExternSignature] = value;
                                notSkippable.Add(value);
                            }
                            else
                            {
                                notSkippable.Remove(value);
                                values.Remove(value);
                                removedThis++;
                            }
                        }
                        else if (!IsPrivate(value))
                        {
                            notSkippable.Add(value);
                        }
                        if (!notSkippable.Contains(value))
                        {
                            values.Remove(value);
                            removedValues++;
                        }
                    }
                }

                // Reprocess all instructions
                for (int i = 0; i < instrs.Count; i++)
                {
                    AssemblyInstruction instr = instrs[i];
                    if (instr is PushInstruction pInst)
                    {
                        if (!notSkippable.Contains(pInst.PushValue))
                        {
                            instrs[i] = TransferInstr(new PushInstruction(GetTempValue(pInst.PushValue, moduleEmitContext.TopTable, tempTable, rootThis)), pInst, hasJump);
                        }
                    }
                    else if (instr is CopyInstruction cInst)
                    {
                        bool newInstr = false;
                        Value sourceValue = cInst.SourceValue;
                        Value targetValue = cInst.TargetValue;
                        if (!notSkippable.Contains(sourceValue))
                        {
                            sourceValue = GetTempValue(sourceValue, moduleEmitContext.TopTable, tempTable, rootThis);
                            newInstr = true;
                        }
                        if (!notSkippable.Contains(targetValue))
                        {
                            targetValue = GetTempValue(targetValue, moduleEmitContext.TopTable, tempTable, rootThis);
                            newInstr = true;
                        }
                        if (newInstr)
                        {
                            instrs[i] = TransferInstr(new CopyInstruction(sourceValue, targetValue), cInst, hasJump);
                        }
                    }
                }
                removedValues -= tempTable.Count; // Add back in the additional variables created
                removedValues -= removedThis; // Not supposed to be in this counter
            }

            if (removedInsts > 0 || removedValues > 0 || removedThis > 0 || tempTable.Count != 0)
            {
                // Add comment to module
                instrs.Insert(0, new Comment($"UdonSharp unoffical optimizer: Removed {removedInsts} instructions, {removedValues} variables, {removedThis} extra __this"));

                // Update addresses and hijack the instructions list
                uint currentAddress = 0;
                Dictionary<uint, uint> addressMap = new Dictionary<uint, uint>();
                foreach (AssemblyInstruction inst in instrs)
                {
                    addressMap[inst.InstructionAddress] = currentAddress;
                    inst.InstructionAddress = currentAddress;
                    currentAddress += inst.Size;
                }
                // Used for the EndAddress of last method
                addressMap.Add(assemblyModule.CurrentAddress, currentAddress);
                _instructions(assemblyModule) = instrs;

                // Add comments to instructions that can be jumped to
                /*
                for (int i = instrs.Count - 1; i >= 0; i--)
                {
                    if (hasJump.Contains(instrs[i]))
                    {
                        instrs.Insert(i, new Comment($"Jump Target: 0x{instrs[i].InstructionAddress:X8}"));
                    }
                }
                //*/

                // Update jump addresses
                foreach (JumpLabel jumpLabel in jumpLabels)
                {
                    if (addressMap.ContainsKey(jumpLabel.Address))
                    {
                        jumpLabel.Address = addressMap[jumpLabel.Address];
                    }
                    else
                    {
                        Debug.LogWarning($"[Optimizer] No address map for JumpLabel: {jumpLabel.Address:X4}");
                    }
                }

                // Update debug info
                List<MethodDebugInfo> methodDebugInfos = _methodDebugInfos(moduleEmitContext.DebugInfo);
                if (methodDebugInfos != null)
                {
                    foreach (MethodDebugInfo mdInfo in methodDebugInfos)
                    {
                        if (addressMap.ContainsKey(mdInfo.methodStartAddress))
                        {
                            mdInfo.methodStartAddress = addressMap[mdInfo.methodStartAddress];
                        }
                        else
                        {
                            Debug.LogWarning($"[Optimizer] No address map for StartAddress: {mdInfo.methodStartAddress:X4}");
                        }
                        if (addressMap.ContainsKey(mdInfo.methodEndAddress))
                        {
                            mdInfo.methodEndAddress = addressMap[mdInfo.methodEndAddress];
                        }
                        else
                        {
                            Debug.LogWarning($"[Optimizer] No address map for EndAddress: {mdInfo.methodEndAddress:X4}");
                        }
                        List<MethodDebugMarker> debugMarkers = mdInfo.debugMarkers;
                        if (debugMarkers != null)
                        {
                            for (int i = 0; i < debugMarkers.Count; i++)
                            {
                                MethodDebugMarker marker = debugMarkers[i];
                                if (marker.startInstruction != -1)
                                {
                                    if (addressMap.ContainsKey((uint)marker.startInstruction))
                                    {
                                        marker.startInstruction = (int)addressMap[(uint)marker.startInstruction];
                                        debugMarkers[i] = marker;
                                    }
                                    else
                                    {
                                        Debug.LogWarning($"[Optimizer] No address map for StartInstruction: {marker.startInstruction:X4}");
                                    }
                                }
                            }
                        }
                    }
                }

                // Update indirect jumps
                // TODO: Hacky, make a more proper way to do this
                foreach (Value value in addrValues)
                {
                    if (addressMap.ContainsKey((uint)value.DefaultValue))
                    {
                        value.DefaultValue = addressMap[(uint)value.DefaultValue];
                    }
                    else
                    {
                        Debug.LogWarning($"[Optimizer] Value {value.UniqueID} not in address map?");
                    }
                }

                foreach (Value value in switchTables)
                {
                    uint[] switchTable = (uint[])value.DefaultValue;
                    for (int i = 0; i < switchTable.Length; i++)
                    {
                        uint oldAddr = switchTable[i];
                        if (addressMap.ContainsKey(oldAddr))
                        {
                            switchTable[i] = addressMap[oldAddr];
                        }
                        else
                        {
                            Debug.LogWarning($"[Optimizer] Value {value.UniqueID}[{i}] not in address map?");
                        }
                    }
                }

                Interlocked.Add(ref removedInstructions, removedInsts);
                Interlocked.Add(ref removedVariables, removedValues);
                Interlocked.Add(ref removedThisTotal, removedThis);
            }
        }

        private static AssemblyInstruction TransferInstr(AssemblyInstruction instr, AssemblyInstruction original, HashSet<AssemblyInstruction> hasJump)
        {
            instr.InstructionAddress = original.InstructionAddress;
            if (hasJump.Contains(original))
            {
                hasJump.Remove(original);
                hasJump.Add(instr);
            }
            return instr;
        }

        private static Value GetTempValue(Value value, ValueTable valueTable, Dictionary<string, Value> tempTable, Dictionary<string, Value> rootThis)
        {
            string udonType = value.UdonType.ExternSignature;
            if (rootThis != null && (value.Flags & Value.ValueFlags.UdonThis) != 0)
            {
                return rootThis[udonType];
            }
            if (tempTable.ContainsKey(udonType))
            {
                return tempTable[udonType];
            }
            Value newValue = new Value(valueTable, $"__temp_{udonType}", value.UserType, Value.ValueFlags.Internal);
            valueTable.Values.Add(newValue);
            tempTable[udonType] = newValue;
            return newValue;
        }
    }
}