/*
 * Unofficial UdonSharp Optimizer
 * The Optimizer.
 * Version 1.0.10
 * Written by BlueAmulet
 */

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UdonSharp.Compiler;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Assembly.Instructions;
using UdonSharp.Compiler.Emit;
using UdonSharpOptimizer.Optimizations;
using UnityEngine;

#pragma warning disable IDE0090 // Use 'new(...)'
#pragma warning disable IDE0063 // Use simple 'using' statement
#pragma warning disable IDE0028 // Simplify collection initialization

namespace UdonSharpOptimizer
{
    internal class Optimizer
    {
        private static readonly OptimizerSettings Settings = OptimizerSettings.Instance;

        private static readonly AccessTools.FieldRef<object, List<AssemblyInstruction>> _instructions = AccessTools.FieldRefAccess<List<AssemblyInstruction>>(typeof(AssemblyModule), "_instructions");
        private static readonly AccessTools.FieldRef<object, List<MethodDebugInfo>> _methodDebugInfos = AccessTools.FieldRefAccess<List<MethodDebugInfo>>(typeof(AssemblyDebugInfo), "_methodDebugInfos");
        private static readonly AccessTools.FieldRef<object, ValueTable> _parentTable = AccessTools.FieldRefAccess<ValueTable>(typeof(Value), "_parentTable");
        private static readonly AccessTools.FieldRef<object, List<ValueTable>> _childTables = AccessTools.FieldRefAccess<List<ValueTable>>(typeof(ValueTable), "_childTables");

        // Various statistics
        private static int _removedInstructions;
        private static int _removedVariables;
        private static int _removedThisTotal;

        // For Settings panel
        public static int RemovedInstructions => _removedInstructions;
        public static int RemovedVariables => _removedVariables;
        public static int RemovedThisTotal => _removedThisTotal;

        // Optimizations
        readonly IBaseOptimization[] _optimizations = new IBaseOptimization[]
        {
            new OPTCopyLoad(),
            new OPTCopyTest(),
            new OPTStoreCopy(),
            new OPTDoubleCopy(),
            new OPTUnreadCopy(),
            new OPTTailCall(),
        };

        // Per program state
        private readonly EmitContext _moduleEmitContext;
        private List<AssemblyInstruction> _instrs;
        private HashSet<AssemblyInstruction> _hasJump;
        private Dictionary<string, Value> _tempTable = new Dictionary<string, Value>();
        internal int removedInsts = 0;

        public Optimizer(EmitContext moduleEmitContext)
        {
            this._moduleEmitContext = moduleEmitContext;
        }

        internal static void ResetGlobalCounters()
        {
            _removedInstructions = 0;
            _removedVariables = 0;
            _removedThisTotal = 0;
        }

        internal static void LogGlobalCounters()
        {
            Debug.Log($"[Optimizer] Removed {_removedInstructions} instructions, {_removedVariables} variables, and {_removedThisTotal} extra __this total");
        }

        /* The Optimizer */
        internal static Comment CopyComment(string code, CopyInstruction cInst)
        {
            return new Comment($"{code}: Removed {cInst.SourceValue.UniqueID} => {cInst.TargetValue.UniqueID} copy");
        }

        internal static bool IsExternWrite(AssemblyInstruction inst)
        {
            if (inst is ExternInstruction extInst)
            {
                return !extInst.Extern.ExternSignature.EndsWith("__SystemVoid");
            }
            return inst is ExternGetInstruction;
        }

        internal static bool IsPrivate(Value value)
        {
            return value.IsInternal || value.IsLocal;
        }

        private static bool IsTemporary(Value value)
        {
            return (value.Flags & Value.ValueFlags.Internal) != 0 || value.IsLocal;
        }

        internal bool HasJump(AssemblyInstruction instr)
        {
            return _hasJump.Contains(instr);
        }

        internal bool HasJump(int idx)
        {
            return _hasJump.Contains(_instrs[idx]);
        }

        internal bool HasJump(int min, int max)
        {
            for (int i = min; i <= max; i++)
            {
                if (_hasJump.Contains(_instrs[i]))
                {
                    return true;
                }
            }
            return false;
        }

        // Full code scan, except optimizable patterns are ignored
        internal bool ReadScan(Func<int, bool> ignore, Value value)
        {
            HashSet<int> ignoreOpt = new HashSet<int>();
            int instrsCount = _instrs.Count;
            for (int i = 0; i < instrsCount; i++)
            {
                if (!ignore(i) && !ignoreOpt.Contains(i))
                {
                    // The last instruction of a method should be RetInstruction, so the missing bound checks should be safe
                    if (_instrs[i] is PushInstruction pInst && pInst.PushValue.UniqueID == value.UniqueID)
                    {
                        // Ignore pushes followed by a extern that returns a value
                        if (!IsExternWrite(_instrs[i + 1]))
                        {
                            return true;
                        }
                        else if (_instrs[i + 2] is CopyInstruction cInst && cInst.SourceValue.UniqueID == value.UniqueID && !HasJump(i + 1, i + 2))
                        {
                            // This should be safe, the variable was JUST overridden, so the read isn't true
                            // Will be cleaned up by OPTStoreCopy
                            ignoreOpt.Add(i + 2);
                        }
                    }
                    else if (_instrs[i] is CopyInstruction cInst && cInst.SourceValue.UniqueID == value.UniqueID)
                    {
                        return true;
                    }
                    else if (_instrs[i] is JumpIfFalseInstruction jifInst && jifInst.ConditionValue.UniqueID == value.UniqueID)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        internal void OptimizeProgram()
        {
            if (!Settings.EnableOptimizer)
            {
                return;
            }

            _instrs = new List<AssemblyInstruction>();
            _hasJump = new HashSet<AssemblyInstruction>();
            AssemblyModule assemblyModule = _moduleEmitContext.Module;
            HashSet<JumpLabel> jumpLabels = new HashSet<JumpLabel>();
            List<Value> addrValues = new List<Value>();
            List<Value> switchTables = new List<Value>();

            // Copy instructions out of module
            for (int i = 0; i < assemblyModule.InstructionCount; i++)
            {
                AssemblyInstruction inst = assemblyModule[i];
                _instrs.Add(inst);
                if (inst is JumpInstruction jInst)
                {
                    jumpLabels.Add(jInst.JumpTarget);
                }
                else if (inst is JumpIfFalseInstruction jifInst)
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
            List<uint> jumpAddress = new List<uint>();
            foreach (JumpLabel jumpLabel in jumpLabels)
            {
                jumpAddress.Add(jumpLabel.Address);
            }
            foreach (Value switchTable in switchTables)
            {
                jumpAddress.AddRange((uint[])switchTable.DefaultValue);
            }
            jumpAddress.Sort((a, b) => a.CompareTo(b));
            List<uint>.Enumerator jumpEnumerator = jumpAddress.GetEnumerator();
            if (jumpEnumerator.MoveNext())
            {
                foreach (AssemblyInstruction inst in _instrs)
                {
                    if (inst.Size != 0 && inst.InstructionAddress == jumpEnumerator.Current)
                    {
                        _hasJump.Add(inst);
                        while (inst.InstructionAddress == jumpEnumerator.Current)
                        {
                            if (!jumpEnumerator.MoveNext())
                            {
                                goto JumpListDone;
                            }
                        }
                    }
                    if (inst.InstructionAddress > jumpEnumerator.Current)
                    {
                        // Shouldn't happen but just in case
                        Debug.LogError("[Optimizer] Jump Target Set Desync");
                        return;
                    }
                }
            }
            JumpListDone:

            // Determine which optimization passes are active
            List<IBaseOptimization> activeOptList = new List<IBaseOptimization>();
            foreach (IBaseOptimization optimization in _optimizations)
            {
                if (optimization.Enabled())
                {
                    activeOptList.Add(optimization);
                }
            }
            IBaseOptimization[] activeOptimizations = activeOptList.ToArray();

            // The actual optimizations
            Dictionary<string, HashSet<uint>> valueBlock = new Dictionary<string, HashSet<uint>>();
            Dictionary<string, uint> valueLast = new Dictionary<string, uint>();
            uint currentBlock = 0;
            for (int i = 0; i < _instrs.Count; i++)
            {
                foreach (IBaseOptimization optimization in activeOptimizations)
                {
                    optimization.ProcessInstruction(this, _instrs, i);
                }

                // Observe what block variables are in for later
                if (Settings.EnableBlockReduction)
                {
                    // If previous instruction is a jump but the next isn't in hasJump, it was a call to another udon function
                    if (_hasJump.Contains(_instrs[i]) || (i > 0 && _instrs[i - 1] is JumpInstruction))
                    {
                        currentBlock++;
                    }
                    Value instrValue = null;
                    Value instrValue2 = null;
                    if (_instrs[i] is SyncTag sInst)
                    {
                        instrValue = sInst.SyncedValue;
                    }
                    else if (_instrs[i] is PushInstruction pInst)
                    {
                        instrValue = pInst.PushValue;
                    }
                    else if (_instrs[i] is CopyInstruction cInst)
                    {
                        instrValue = cInst.SourceValue;
                        instrValue2 = cInst.TargetValue;
                    }
                    else if (_instrs[i] is JumpIfFalseInstruction jifInst)
                    {
                        instrValue = jifInst.ConditionValue;
                    }
                    else if (_instrs[i] is JumpIndirectInstruction jiInst)
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
                        valueLast[variableName] = _instrs[i].InstructionAddress;
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
            //Debug.Log($"Removed {removedInsts} instructions");

            // Pass 2: Attempt to reduce the number of temporary variables
            int removedValues = 0;
            int removedThis = 0;
            _tempTable = new Dictionary<string, Value>();
            if (Settings.EnableVariableReduction)
            {
                HashSet<Value> notSkippable = new HashSet<Value>();
                HashSet<CopyInstruction> ignoreCopyRead = new HashSet<CopyInstruction>();
                Dictionary<string, HashSet<uint>> blockCounters = new Dictionary<string, HashSet<uint>>();
                Dictionary<string, Value> tempMap = new Dictionary<string, Value>();
                for (int i = 0; i < _instrs.Count; i++)
                {
                    AssemblyInstruction instr = _instrs[i];
                    int skip = 0;
                    // If previous instruction is a jump but the next isn't in hasJump, it was a call to another udon function
                    if (_hasJump.Contains(instr) || (i > 0 && _instrs[i - 1] is JumpInstruction))
                    {
                        blockCounters.Clear();
                    }
                    if (instr is SyncTag sInst)
                    {
                        notSkippable.Add(sInst.SyncedValue);
                        if (BlockScopeRemap(sInst.SyncedValue, sInst.InstructionAddress, valueBlock, valueLast, notSkippable, blockCounters, tempMap, _moduleEmitContext.TopTable, out Value outValue))
                        {
                            _instrs[i] = TransferInstr(new SyncTag(outValue, sInst.SyncMode), sInst);
                        }
                    }
                    else if (instr is PushInstruction pInst)
                    {
                        if (Settings.EnableStoreLoad)
                        {
                            // Check for extern write + read
                            if (!IsExternWrite(_instrs[i + 1]) || _hasJump.Contains(_instrs[i + 1]))
                            {
                                notSkippable.Add(pInst.PushValue);
                            }
                            else if (_instrs[i + 2] is PushInstruction pInst2)
                            {
                                if (pInst.PushValue == pInst2.PushValue && !_hasJump.Contains(pInst2))
                                {
                                    // Skip it
                                    skip = 2;
                                }
                                else
                                {
                                    notSkippable.Add(pInst.PushValue);
                                }
                            }
                            else if (_instrs[i + 2] is CopyInstruction cInst)
                            {
                                if (pInst.PushValue == cInst.SourceValue && !_hasJump.Contains(cInst))
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
                            else
                            {
                                notSkippable.Add(pInst.PushValue);
                            }
                        }
                        else
                        {
                            notSkippable.Add(pInst.PushValue);
                        }
                        if (BlockScopeRemap(pInst.PushValue, pInst.InstructionAddress, valueBlock, valueLast, notSkippable, blockCounters, tempMap, _moduleEmitContext.TopTable, out Value outValue))
                        {
                            _instrs[i] = TransferInstr(new PushInstruction(outValue), pInst);
                            skip = 0;
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
                            if (_instrs[i + 1] is PushInstruction pInst2)
                            {
                                if (cInst.TargetValue == pInst2.PushValue && !_hasJump.Contains(pInst2))
                                {
                                    // Skip
                                    skip = 1;
                                }
                                else
                                {
                                    notSkippable.Add(cInst.TargetValue);
                                }
                            }
                            else if (_instrs[i + 1] is CopyInstruction cInst2)
                            {
                                if (cInst.TargetValue == cInst2.SourceValue && !_hasJump.Contains(cInst2))
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
                        else
                        {
                            notSkippable.Add(cInst.SourceValue);
                            notSkippable.Add(cInst.TargetValue);
                        }
                        bool needNewCopy = false;
                        Value copySource = cInst.SourceValue;
                        Value copyTarget = cInst.TargetValue;
                        if (BlockScopeRemap(cInst.SourceValue, cInst.InstructionAddress, valueBlock, valueLast, notSkippable, blockCounters, tempMap, _moduleEmitContext.TopTable, out Value outValueS))
                        {
                            copySource = outValueS;
                            needNewCopy = true;
                        }
                        if (BlockScopeRemap(cInst.TargetValue, cInst.InstructionAddress, valueBlock, valueLast, notSkippable, blockCounters, tempMap, _moduleEmitContext.TopTable, out Value outValueT))
                        {
                            copyTarget = outValueT;
                            needNewCopy = true;
                        }
                        if (needNewCopy)
                        {
                            _instrs[i] = TransferInstr(new CopyInstruction(copySource, copyTarget), cInst);
                            skip = 0;
                        }
                    }
                    else if (instr is JumpIfFalseInstruction jifInst)
                    {
                        notSkippable.Add(jifInst.ConditionValue);
                        if (BlockScopeRemap(jifInst.ConditionValue, jifInst.InstructionAddress, valueBlock, valueLast, notSkippable, blockCounters, tempMap, _moduleEmitContext.TopTable, out Value outValue))
                        {
                            _instrs[i] = TransferInstr(new JumpIfFalseInstruction(jifInst.JumpTarget, outValue), jifInst);
                        }
                    }
                    else if (instr is JumpIndirectInstruction jiInst)
                    {
                        notSkippable.Add(jiInst.JumpTargetValue);
                        if (BlockScopeRemap(jiInst.JumpTargetValue, jiInst.InstructionAddress, valueBlock, valueLast, notSkippable, blockCounters, tempMap, _moduleEmitContext.TopTable, out Value outValue))
                        {
                            _instrs[i] = TransferInstr(new JumpIndirectInstruction(outValue), jiInst);
                        }
                    }
                    else if (instr is RetInstruction rInst)
                    {
                        notSkippable.Add(rInst.RetValRef);
                    }
                    i += skip;
                }

                // Gather all tables in the module
                HashSet<ValueTable> tables = new HashSet<ValueTable>();
                HashSet<ValueTable> searchTable = new HashSet<ValueTable>() { assemblyModule.RootTable };
                HashSet<ValueTable> nextSearch = new HashSet<ValueTable>();
                do
                {
                    foreach (ValueTable table in searchTable)
                    {
                        tables.Add(table);
                        List<ValueTable> childTables = _childTables(table);
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
                for (int i = 0; i < _instrs.Count; i++)
                {
                    AssemblyInstruction instr = _instrs[i];
                    if (instr is PushInstruction pInst)
                    {
                        if (!notSkippable.Contains(pInst.PushValue))
                        {
                            _instrs[i] = TransferInstr(new PushInstruction(GetTempValue(pInst.PushValue, rootThis)), pInst);
                        }
                    }
                    else if (instr is CopyInstruction cInst)
                    {
                        bool newInstr = false;
                        Value sourceValue = cInst.SourceValue;
                        Value targetValue = cInst.TargetValue;
                        if (!notSkippable.Contains(sourceValue))
                        {
                            sourceValue = GetTempValue(sourceValue, rootThis);
                            newInstr = true;
                        }
                        if (!notSkippable.Contains(targetValue))
                        {
                            targetValue = GetTempValue(targetValue, rootThis);
                            newInstr = true;
                        }
                        if (newInstr)
                        {
                            _instrs[i] = TransferInstr(new CopyInstruction(sourceValue, targetValue), cInst);
                        }
                    }
                }
                removedValues -= _tempTable.Count; // Add back in the additional variables created
                removedValues -= removedThis; // Not supposed to be in this counter
            }

            if (removedInsts > 0 || removedValues > 0 || removedThis > 0 || _tempTable.Count != 0)
            {
                // Add comment to module
                _instrs.Insert(0, new Comment($"UdonSharp unofficial optimizer: Removed {removedInsts} instructions, {removedValues} variables, {removedThis} extra __this"));

                // Update addresses and hijack the instructions list
                uint currentAddress = 0;
                Dictionary<uint, uint> addressMap = new Dictionary<uint, uint>();
                foreach (AssemblyInstruction inst in _instrs)
                {
                    addressMap[inst.InstructionAddress] = currentAddress;
                    inst.InstructionAddress = currentAddress;
                    currentAddress += inst.Size;
                }
                // Used for the EndAddress of last method
                addressMap.Add(assemblyModule.CurrentAddress, currentAddress);
                _instructions(assemblyModule) = _instrs;

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
                    if (addressMap.TryGetValue(jumpLabel.Address, out uint address))
                    {
                        jumpLabel.Address = address;
                    }
                    else
                    {
                        Debug.LogWarning($"[Optimizer] No address map for JumpLabel: {jumpLabel.Address:X4}");
                    }
                }

                // Update debug info
                List<MethodDebugInfo> methodDebugInfos = _methodDebugInfos(_moduleEmitContext.DebugInfo);
                if (methodDebugInfos != null)
                {
                    foreach (MethodDebugInfo mdInfo in methodDebugInfos)
                    {
                        if (addressMap.TryGetValue(mdInfo.methodStartAddress, out uint startAddress))
                        {
                            mdInfo.methodStartAddress = startAddress;
                        }
                        else
                        {
                            Debug.LogWarning($"[Optimizer] No address map for StartAddress: {mdInfo.methodStartAddress:X4}");
                        }
                        if (addressMap.TryGetValue(mdInfo.methodEndAddress, out uint endAddress))
                        {
                            mdInfo.methodEndAddress = endAddress;
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
                        if (addressMap.TryGetValue(oldAddr, out uint address))
                        {
                            switchTable[i] = address;
                        }
                        else
                        {
                            Debug.LogWarning($"[Optimizer] Value {value.UniqueID}[{i}] not in address map?");
                        }
                    }
                }

                Interlocked.Add(ref _removedInstructions, removedInsts);
                Interlocked.Add(ref _removedVariables, removedValues);
                Interlocked.Add(ref _removedThisTotal, removedThis);
            }
        }

        private bool BlockScopeRemap(Value value, uint instrAddr, IReadOnlyDictionary<string, HashSet<uint>> valueBlock, IReadOnlyDictionary<string, uint> valueLast, ISet<Value> notSkippable, IDictionary<string, HashSet<uint>> blockCounters, IDictionary<string, Value> tempMap, ValueTable valueTable, out Value outValue)
        {
            string variableID = value.UniqueID;
            if (Settings.EnableBlockReduction && IsTemporary(value) && valueBlock[variableID].Count == 1)
            {
                if (!tempMap.ContainsKey(variableID))
                {
                    string udonType = value.UdonType.ExternSignature;
                    if (!blockCounters.TryGetValue(udonType, out HashSet<uint> counterUsed))
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
                    if (!_tempTable.ContainsKey(tempName))
                    {
                        _tempTable[tempName] = new Value(_parentTable(value), tempName, value.UserType, Value.ValueFlags.Internal);
                        valueTable.Values.Add(_tempTable[tempName]);
                        notSkippable.Add(_tempTable[tempName]);
                    }
                    tempMap[variableID] = _tempTable[tempName];
                }
                notSkippable.Remove(value);
                outValue = tempMap[variableID];
                // Free counter for later use if past last usage of variable
                if (valueLast[variableID] <= instrAddr)
                {
                    string tempName = outValue.UniqueID;
                    uint counter = uint.Parse(tempName.Substring(tempName.LastIndexOf('_')+1));
                    string udonType = value.UdonType.ExternSignature;
                    blockCounters[udonType].Remove(counter);
                }
                return true;
            }
            else
            {
                outValue = value;
                return false;
            }
        }

        internal AssemblyInstruction TransferInstr(AssemblyInstruction instr, AssemblyInstruction original)
        {
            instr.InstructionAddress = original.InstructionAddress;
            if (_hasJump.Contains(original))
            {
                _hasJump.Remove(original);
                _hasJump.Add(instr);
            }
            return instr;
        }

        private Value GetTempValue(Value value, Dictionary<string, Value> rootThis)
        {
            string udonType = value.UdonType.ExternSignature;
            if (rootThis != null && (value.Flags & Value.ValueFlags.UdonThis) != 0)
            {
                return rootThis[udonType];
            }
            if (_tempTable.TryGetValue(udonType, out Value tempValue))
            {
                return tempValue;
            }
            ValueTable valueTable = _moduleEmitContext.TopTable;
            Value newValue = new Value(valueTable, $"__temp_{udonType}", value.UserType, Value.ValueFlags.Internal);
            valueTable.Values.Add(newValue);
            _tempTable[udonType] = newValue;
            return newValue;
        }
    }
}