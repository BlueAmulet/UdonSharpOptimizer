/*
 * Unofficial UdonSharp Optimizer
 * Written by BlueAmulet
 */

using HarmonyLib;
using System;
using System.Collections.Generic;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Assembly.Instructions;
using UdonSharp.Compiler.Emit;

namespace UdonSharpOptimizer.Passes
{
    // Handles reducing excessive temporary variables
    // Remaps temporaries to shared variables per block
    // Remaps all store-load only variables to shared variables
    // Fixes extra __this variables
    internal sealed class VariableReductionPass : IOptimizerPass
    {
        private static readonly AccessTools.FieldRef<object, ValueTable> _parentTable = AccessTools.FieldRefAccess<ValueTable>(typeof(Value), "_parentTable");
        private static readonly AccessTools.FieldRef<object, List<ValueTable>> _childTables = AccessTools.FieldRefAccess<List<ValueTable>>(typeof(ValueTable), "_childTables");

        private static OptimizerSettings Settings => OptimizerSettings.Instance;

        private static readonly string[] PossibleReentrant = {
            ".__SendCustomEvent__",
            ".__SetProgramVariable__",
            ".__SendCustomNetworkEvent__"
        };

        public void Execute(OptimizerContext context)
        {
            if (!Settings.EnableVariableReduction)
            {
                return;
            }

            // Record the block that each variable is used in
            Dictionary<string, ISet<uint>> valueBlock = new Dictionary<string, ISet<uint>>();
            Dictionary<string, uint> valueLast = new Dictionary<string, uint>();
            RecordBlockUsage(context, valueBlock, valueLast);

            // Determine store-load variables
            ISet<Value> notSkippable = GatherNotSkippable(context);

            // Remap all temporary variables that are in a single block
            RemapBlockTemporaries(context, valueBlock, valueLast, notSkippable);

            // Remove variables from the tables marked skippable
            // Determine which variable is the root __this per type
            ISet<ValueTable> tables = GatherTables(context);
            Dictionary<string, Value> rootThis = RemoveSkippableValues(context, tables, notSkippable);

            // Replace any still remaining skippable variables with shared temporaries
            // Fix excessive __this variables
            RemapSharedTemporaries(context, notSkippable, rootThis);

            context.RemovedValues -= context.TempTable.Count; // Add back in the additional variables created
            context.RemovedValues -= context.RemovedThis; // Not supposed to be in this counter
        }

        private static void RecordBlockUsage(OptimizerContext context, IDictionary<string, ISet<uint>> valueBlock, IDictionary<string, uint> valueLast)
        {
            if (!Settings.EnableBlockReduction)
            {
                return;
            }

            // Observe what block variables are in for later
            uint currentBlock = 0;
            IList<AssemblyInstruction> instrs = context.Instrs;
            for (int i = 0; i < instrs.Count; i++)
            {
                AssemblyInstruction instr = instrs[i];
                if (IsBlockBoundary(context, i, instr))
                {
                    currentBlock++;
                }
                Value instrValue = null;
                Value instrValue2 = null;
                if (instr is SyncTag sInst)
                {
                    instrValue = sInst.SyncedValue;
                }
                else if (instr is PushInstruction pInst)
                {
                    instrValue = pInst.PushValue;
                }
                else if (instr is CopyInstruction cInst)
                {
                    instrValue = cInst.SourceValue;
                    instrValue2 = cInst.TargetValue;
                }
                else if (instr is JumpIfFalseInstruction jifInst)
                {
                    instrValue = jifInst.ConditionValue;
                }
                else if (instr is JumpIndirectInstruction jiInst)
                {
                    instrValue = jiInst.JumpTargetValue;
                }
                while (instrValue != null)
                {
                    string variableName = instrValue.UniqueID;
                    if (!valueBlock.ContainsKey(variableName))
                    {
                        valueBlock[variableName] = new HashSet<uint>();
                    }
                    valueBlock[variableName].Add(currentBlock);
                    valueLast[variableName] = instr.InstructionAddress;
                    // Check second value of copy instructions
                    if (instrValue2 == null)
                    {
                        break;
                    }
                    instrValue = instrValue2;
                    instrValue2 = null;
                }
            }
        }

        private static ISet<Value> GatherNotSkippable(OptimizerContext context)
        {
            ISet<Value> notSkippable = new HashSet<Value>();
            ISet<CopyInstruction> ignoreCopyRead = new HashSet<CopyInstruction>();
            IList<AssemblyInstruction> instrs = context.Instrs;
            for (int i = 0; i < instrs.Count; i++)
            {
                int skip = 0;
                AssemblyInstruction instr = instrs[i];
                if (instr is SyncTag sInst)
                {
                    notSkippable.Add(sInst.SyncedValue);
                }
                else if (instr is PushInstruction pInst)
                {
                    if (Settings.EnableStoreLoad)
                    {
                        // Check for extern write + read
                        if (!Optimizer.IsExternWrite(instrs[i + 1]) || context.HasJumpSet.Contains(instrs[i + 1]) || context.HasJumpSet.Contains(instrs[i + 2]))
                        {
                            notSkippable.Add(pInst.PushValue);
                        }
                        else if (instrs[i + 2] is PushInstruction pInst2)
                        {
                            if (pInst.PushValue == pInst2.PushValue)
                            {
                                skip = 2;
                            }
                            else
                            {
                                notSkippable.Add(pInst.PushValue);
                            }
                        }
                        else if (instrs[i + 2] is CopyInstruction cInst)
                        {
                            if (pInst.PushValue == cInst.SourceValue)
                            {
                                // Skip extern but ignore copy's read next loop
                                skip = 1;
                                ignoreCopyRead.Add(cInst);
                            }
                            else
                            {
                                notSkippable.Add(pInst.PushValue);
                            }
                        }
                        else if (instrs[i + 2] is JumpIfFalseInstruction jifInst)
                        {
                            if (pInst.PushValue == jifInst.ConditionValue)
                            {
                                skip = 2;
                            }
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
                    if (Settings.EnableStoreLoad)
                    {
                        if (!ignoreCopyRead.Contains(cInst))
                        {
                            notSkippable.Add(cInst.SourceValue);
                        }
                        if (context.HasJumpSet.Contains(instrs[i + 1]))
                        {
                            notSkippable.Add(cInst.TargetValue);
                        }
                        else if (instrs[i + 1] is PushInstruction pInst2)
                        {
                            if (cInst.TargetValue == pInst2.PushValue)
                            {
                                skip = 1;
                            }
                            else
                            {
                                notSkippable.Add(cInst.TargetValue);
                            }
                        }
                        else if (instrs[i + 1] is CopyInstruction cInst2)
                        {
                            if (cInst.TargetValue == cInst2.SourceValue)
                            {
                                // Skip the read of the next copy instruction
                                ignoreCopyRead.Add(cInst2);
                            }
                            else
                            {
                                notSkippable.Add(cInst.TargetValue);
                            }
                        }
                        else if (instrs[i + 1] is JumpIfFalseInstruction jifInst)
                        {
                            if (cInst.TargetValue == jifInst.ConditionValue)
                            {
                                skip = 1;
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
                    else
                    {
                        notSkippable.Add(cInst.SourceValue);
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
                i += skip;
            }
            return notSkippable;
        }

        public static void RemapBlockTemporaries(OptimizerContext context, IReadOnlyDictionary<string, ISet<uint>> valueBlock, IReadOnlyDictionary<string, uint> valueLast, ISet<Value> notSkippable)
        {
            IDictionary<string, ISet<uint>> blockCounters = new Dictionary<string, ISet<uint>>();
            IDictionary<string, Value> tempMap = new Dictionary<string, Value>();

            IList<AssemblyInstruction> instrs = context.Instrs;
            for (int i = 0; i < instrs.Count; i++)
            {
                AssemblyInstruction instr = instrs[i];
                if (IsBlockBoundary(context, i, instr))
                {
                    blockCounters.Clear();
                }

                if (instr is SyncTag sInst)
                {
                    if (BlockScopeRemap(context, sInst.SyncedValue, sInst.InstructionAddress, valueBlock, valueLast, notSkippable, blockCounters, tempMap, out Value outValue))
                    {
                        instrs[i] = context.TransferInstr(new SyncTag(outValue, sInst.SyncMode), sInst);
                    }
                }
                else if (instr is PushInstruction pInst)
                {
                    if (true)
                    {
                    }
                    if (BlockScopeRemap(context, pInst.PushValue, pInst.InstructionAddress, valueBlock, valueLast, notSkippable, blockCounters, tempMap, out Value outValue))
                    {
                        instrs[i] = context.TransferInstr(new PushInstruction(outValue), pInst);
                    }
                }
                else if (instr is CopyInstruction cInst)
                {
                    bool needNewCopy = false;
                    Value copySource = cInst.SourceValue;
                    Value copyTarget = cInst.TargetValue;
                    if (BlockScopeRemap(context, cInst.SourceValue, cInst.InstructionAddress, valueBlock, valueLast, notSkippable, blockCounters, tempMap, out Value outValueS))
                    {
                        copySource = outValueS;
                        needNewCopy = true;
                    }
                    if (BlockScopeRemap(context, cInst.TargetValue, cInst.InstructionAddress, valueBlock, valueLast, notSkippable, blockCounters, tempMap, out Value outValueT))
                    {
                        copyTarget = outValueT;
                        needNewCopy = true;
                    }
                    if (needNewCopy)
                    {
                        instrs[i] = context.TransferInstr(new CopyInstruction(copySource, copyTarget), cInst);
                    }
                }
                else if (instr is JumpIfFalseInstruction jifInst)
                {
                    if (BlockScopeRemap(context, jifInst.ConditionValue, jifInst.InstructionAddress, valueBlock, valueLast, notSkippable, blockCounters, tempMap, out Value outValue))
                    {
                        instrs[i] = context.TransferInstr(new JumpIfFalseInstruction(jifInst.JumpTarget, outValue), jifInst);
                    }
                }
                else if (instr is JumpIndirectInstruction jiInst)
                {
                    if (BlockScopeRemap(context, jiInst.JumpTargetValue, jiInst.InstructionAddress, valueBlock, valueLast, notSkippable, blockCounters, tempMap, out Value outValue))
                    {
                        instrs[i] = context.TransferInstr(new JumpIndirectInstruction(outValue), jiInst);
                    }
                }
            }
        }

        private static bool IsBlockBoundary(OptimizerContext context, int i, AssemblyInstruction instr)
        {
            // If previous instruction is a jump but the next isn't in HasJumpSet, it was a call to another udon function
            if (context.HasJumpSet.Contains(instr) || (i > 0 && context.Instrs[i - 1] is JumpInstruction))
            {
                return true;
            }
            // Check if this instruction calls to another udon behaviour
            if (instr is ExternInstruction extInst)
            {
                string signature = extInst.Extern.ExternSignature;
                foreach (string funcName in PossibleReentrant)
                {
                    if (signature.Contains(funcName))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool BlockScopeRemap(OptimizerContext context, Value value, uint instrAddr, IReadOnlyDictionary<string, ISet<uint>> valueBlock, IReadOnlyDictionary<string, uint> valueLast, ISet<Value> notSkippable, IDictionary<string, ISet<uint>> blockCounters, IDictionary<string, Value> tempMap, out Value outValue)
        {
            string variableID = value.UniqueID;
            if (!Settings.EnableBlockReduction || !IsTemporary(value) || valueBlock[variableID].Count != 1)
            {
                outValue = value;
                return false;
            }
            if (!tempMap.ContainsKey(variableID))
            {
                string udonType = value.UdonType.ExternSignature;
                if (!blockCounters.TryGetValue(udonType, out ISet<uint> counterUsed))
                {
                    counterUsed = new HashSet<uint>();
                    blockCounters[udonType] = counterUsed;
                }
                uint counter = 0;
                while (counterUsed.Contains(counter))
                {
                    counter++;
                }
                counterUsed.Add(counter);
                string tempName = $"__temp_{udonType}_{counter}";
                if (!context.TempTable.ContainsKey(tempName))
                {
                    context.TempTable[tempName] = new Value(_parentTable(value), tempName, value.UserType, Value.ValueFlags.Internal);
                    context.ModuleEmitContext.TopTable.Values.Add(context.TempTable[tempName]);
                    notSkippable.Add(context.TempTable[tempName]);
                }
                tempMap[variableID] = context.TempTable[tempName];
            }
            notSkippable.Remove(value);
            outValue = tempMap[variableID];
            // Free counter for later use if past last usage of variable
            if (valueLast[variableID] <= instrAddr)
            {
                string tempName = outValue.UniqueID;
                uint counter = uint.Parse(tempName.Substring(tempName.LastIndexOf('_') + 1));
                string udonType = value.UdonType.ExternSignature;
                blockCounters[udonType].Remove(counter);
            }
            return true;
        }

        private static bool IsTemporary(Value value)
        {
            return (value.Flags & Value.ValueFlags.Internal) != 0 || value.IsLocal;
        }

        private static ISet<ValueTable> GatherTables(OptimizerContext context)
        {
            // Gather all tables in the module
            ISet<ValueTable> tables = new HashSet<ValueTable>();
            ISet<ValueTable> searchTable = new HashSet<ValueTable> { context.AssemblyModule.RootTable };
            ISet<ValueTable> nextSearch = new HashSet<ValueTable>();
            do
            {
                foreach (ValueTable table in searchTable)
                {
                    tables.Add(table);
                    IList<ValueTable> childTables = _childTables(table);
                    if (childTables != null)
                    {
                        foreach (ValueTable childTable in childTables)
                        {
                            nextSearch.Add(childTable);
                        }
                    }
                }
                (nextSearch, searchTable) = (searchTable, nextSearch);
                nextSearch.Clear();
            } while (searchTable.Count != 0);
            return tables;
        }

        private static Dictionary<string, Value> RemoveSkippableValues(OptimizerContext context, ISet<ValueTable> tables, ISet<Value> notSkippable)
        {
            // Remove all values that can be reduced to a single temporary
            Dictionary<string, Value> rootThis = null;
            if (Settings.EnableThisBugFix)
            {
                rootThis = new Dictionary<string, Value>();
            }
            foreach (ValueTable table in tables)
            {
                List<Value> values = table.Values;
                foreach (Value value in values.ToArray())
                {
                    if (Settings.EnableThisBugFix && (value.Flags & Value.ValueFlags.UdonThis) != 0)
                    {
                        if (value.UniqueID.EndsWith("_0", StringComparison.Ordinal))
                        {
                            rootThis[value.UdonType.ExternSignature] = value;
                            notSkippable.Add(value);
                        }
                        else
                        {
                            notSkippable.Remove(value);
                            values.Remove(value);
                            context.RemovedThis++;
                        }
                    }
                    else if (!Optimizer.IsPrivate(value))
                    {
                        notSkippable.Add(value);
                    }
                    if (!notSkippable.Contains(value))
                    {
                        values.Remove(value);
                        context.RemovedValues++;
                    }
                }
            }
            return rootThis;
        }

        private static void RemapSharedTemporaries(OptimizerContext context, ISet<Value> notSkippable, IReadOnlyDictionary<string, Value> rootThis)
        {
            // Reprocess all instructions
            IList<AssemblyInstruction> instrs = context.Instrs;
            for (int i = 0; i < instrs.Count; i++)
            {
                AssemblyInstruction instr = instrs[i];
                if (instr is PushInstruction pInst)
                {
                    if (!notSkippable.Contains(pInst.PushValue))
                    {
                        instrs[i] = context.TransferInstr(new PushInstruction(GetTempValue(context, pInst.PushValue, rootThis)), pInst);
                    }
                }
                else if (instr is CopyInstruction cInst)
                {
                    bool newInstr = false;
                    Value sourceValue = cInst.SourceValue;
                    Value targetValue = cInst.TargetValue;
                    if (!notSkippable.Contains(sourceValue))
                    {
                        sourceValue = GetTempValue(context, sourceValue, rootThis);
                        newInstr = true;
                    }
                    if (!notSkippable.Contains(targetValue))
                    {
                        targetValue = GetTempValue(context, targetValue, rootThis);
                        newInstr = true;
                    }
                    if (newInstr)
                    {
                        instrs[i] = context.TransferInstr(new CopyInstruction(sourceValue, targetValue), cInst);
                    }
                }
                else if (instr is JumpIfFalseInstruction jifInst)
                {
                    if (!notSkippable.Contains(jifInst.ConditionValue))
                    {
                        instrs[i] = context.TransferInstr(new JumpIfFalseInstruction(jifInst.JumpTarget, GetTempValue(context, jifInst.ConditionValue, rootThis)), jifInst);
                    }
                }
            }
        }

        private static Value GetTempValue(OptimizerContext context, Value value, IReadOnlyDictionary<string, Value> rootThis)
        {
            string udonType = value.UdonType.ExternSignature;
            if (rootThis != null && (value.Flags & Value.ValueFlags.UdonThis) != 0)
            {
                return rootThis[udonType];
            }
            if (context.TempTable.TryGetValue(udonType, out Value tempValue))
            {
                return tempValue;
            }
            ValueTable valueTable = context.ModuleEmitContext.TopTable;
            Value newValue = new Value(valueTable, $"__temp_{udonType}", value.UserType, Value.ValueFlags.Internal);
            valueTable.Values.Add(newValue);
            context.TempTable[udonType] = newValue;
            return newValue;
        }
    }
}