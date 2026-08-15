/*
 * Unofficial UdonSharp Optimizer
 * Written by BlueAmulet
 */

using System;
using System.Collections.Generic;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Assembly.Instructions;
using UdonSharp.Compiler.Emit;

namespace UdonSharpOptimizer
{
    // Shared context between all optimizer passes
    internal sealed class OptimizerContext
    {
        // Per program state
        public readonly EmitContext ModuleEmitContext;
        public AssemblyModule AssemblyModule => ModuleEmitContext.Module;

        public readonly List<AssemblyInstruction> Instrs;
        public readonly ISet<AssemblyInstruction> HasJumpSet;
        public readonly IDictionary<uint, AssemblyInstruction> InstrMap;

        public readonly ISet<JumpLabel> JumpLabels;
        public readonly IList<Value> AddrValues;
        public readonly IList<Value> SwitchTables;

        public readonly IDictionary<string, Value> TempTable;

        // Statistics per program
        public int RemovedInstrs;
        public int RemovedValues;
        public int RemovedThis;

        internal OptimizerContext(EmitContext moduleEmitContext)
        {
            ModuleEmitContext = moduleEmitContext;

            Instrs = new List<AssemblyInstruction>();
            HasJumpSet = new HashSet<AssemblyInstruction>();
            InstrMap = new Dictionary<uint, AssemblyInstruction>();

            JumpLabels = new HashSet<JumpLabel>();
            AddrValues = new List<Value>();
            SwitchTables = new List<Value>();

            TempTable = new Dictionary<string, Value>();
        }

        internal bool HasJump(AssemblyInstruction instr)
        {
            return HasJumpSet.Contains(instr);
        }

        internal bool HasJump(int idx)
        {
            return HasJumpSet.Contains(Instrs[idx]);
        }

        internal bool HasJump(int min, int max)
        {
            for (int i = min; i <= max; i++)
            {
                if (HasJumpSet.Contains(Instrs[i]))
                {
                    return true;
                }
            }
            return false;
        }

        // Full code scan, except optimizable patterns are ignored
        internal bool ReadScan(Func<int, bool> ignore, Value value)
        {
            ISet<int> ignoreOpt = new HashSet<int>();
            int instrsCount = Instrs.Count;
            for (int i = 0; i < instrsCount; i++)
            {
                if (ignore(i) || ignoreOpt.Contains(i))
                {
                    continue;
                }
                // The last instruction of a method should be RetInstruction, so the missing bound checks should be safe
                if (Instrs[i] is PushInstruction pInst && pInst.PushValue.UniqueID == value.UniqueID)
                {
                    // Ignore pushes followed by a extern that returns a value
                    if (!Optimizer.IsExternWrite(Instrs[i + 1]))
                    {
                        return true;
                    }
                    else if (Instrs[i + 2] is CopyInstruction cInst && cInst.SourceValue.UniqueID == value.UniqueID && !HasJump(i + 1, i + 2))
                    {
                        // This should be safe, the variable was JUST overridden, so the read isn't true
                        // Will be cleaned up by OPTStoreCopy
                        ignoreOpt.Add(i + 2);
                    }
                }
                else if (Instrs[i] is CopyInstruction cInst && cInst.SourceValue.UniqueID == value.UniqueID)
                {
                    return true;
                }
                else if (Instrs[i] is JumpIfFalseInstruction jifInst && jifInst.ConditionValue.UniqueID == value.UniqueID)
                {
                    return true;
                }
            }
            return false;
        }

        internal AssemblyInstruction TransferInstr(AssemblyInstruction instr, AssemblyInstruction original)
        {
            instr.InstructionAddress = original.InstructionAddress;
            if (HasJumpSet.Contains(original))
            {
                HasJumpSet.Remove(original);
                HasJumpSet.Add(instr);
                InstrMap[instr.InstructionAddress] = instr;
            }
            return instr;
        }
    }
}