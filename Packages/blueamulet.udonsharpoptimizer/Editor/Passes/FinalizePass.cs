/*
 * Unofficial UdonSharp Optimizer
 * Written by BlueAmulet
 */

using HarmonyLib;
using System.Collections.Generic;
using UdonSharp.Compiler;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Assembly.Instructions;
using UdonSharp.Compiler.Emit;
using UnityEngine;

namespace UdonSharpOptimizer.Passes
{
    // Commits any changes made back to the module
    // Recalculates instructions addresses
    // Fixes jump labels, debug info, return addresses, and switch tables
    internal sealed class FinalizePass : IOptimizerPass
    {
        private static readonly AccessTools.FieldRef<object, List<AssemblyInstruction>> _instructions = AccessTools.FieldRefAccess<List<AssemblyInstruction>>(typeof(AssemblyModule), "_instructions");
        private static readonly AccessTools.FieldRef<object, List<MethodDebugInfo>> _methodDebugInfos = AccessTools.FieldRefAccess<List<MethodDebugInfo>>(typeof(AssemblyDebugInfo), "_methodDebugInfos");

        public void Execute(OptimizerContext context)
        {
            if (context.RemovedInstrs == 0 && context.RemovedValues == 0 && context.RemovedThis == 0 && context.TempTable.Count == 0)
            {
                return;
            }

            // Add comment to module
            context.Instrs.Insert(0, new Comment($"UdonSharp unofficial optimizer: Removed {context.RemovedInstrs} instructions, {context.RemovedValues} variables, {context.RemovedThis} extra __this"));

            // Update addresses and hijack the instructions list
            uint currentAddress = 0;
            Dictionary<uint, uint> addressMap = new Dictionary<uint, uint>();
            foreach (AssemblyInstruction inst in context.Instrs)
            {
                addressMap[inst.InstructionAddress] = currentAddress;
                inst.InstructionAddress = currentAddress;
                currentAddress += inst.Size;
            }
            // Used for the EndAddress of last method
            AssemblyModule assemblyModule = context.AssemblyModule;
            addressMap.Add(assemblyModule.CurrentAddress, currentAddress);
            _instructions(assemblyModule) = context.Instrs;

            UpdateJumpLabels(context, addressMap);
            UpdateDebugInfo(context, addressMap);
            UpdateIndirectJumps(context, addressMap);
            UpdateSwitchTables(context, addressMap);

            Optimizer.AddGlobalCounters(context);
        }

        private static void UpdateJumpLabels(OptimizerContext context, IReadOnlyDictionary<uint, uint> addressMap)
        {
            foreach (JumpLabel jumpLabel in context.JumpLabels)
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
        }

        private static void UpdateDebugInfo(OptimizerContext context, IReadOnlyDictionary<uint, uint> addressMap)
        {
            List<MethodDebugInfo> methodDebugInfos = _methodDebugInfos(context.ModuleEmitContext.DebugInfo);
            if (methodDebugInfos == null)
            {
                return;
            }
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
                if (debugMarkers == null)
                {
                    continue;
                }
                for (int i = 0; i < debugMarkers.Count; i++)
                {
                    MethodDebugMarker marker = debugMarkers[i];
                    if (marker.startInstruction == -1)
                    {
                        continue;
                    }
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

        private static void UpdateIndirectJumps(OptimizerContext context, IReadOnlyDictionary<uint, uint> addressMap)
        {
            foreach (Value value in context.AddrValues)
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
        }

        private static void UpdateSwitchTables(OptimizerContext context, IReadOnlyDictionary<uint, uint> addressMap)
        {
            foreach (Value value in context.SwitchTables)
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
        }
    }
}