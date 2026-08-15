/*
 * Unofficial UdonSharp Optimizer
 * Written by BlueAmulet
 */

using System;
using System.Collections.Generic;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Assembly.Instructions;
using UdonSharp.Compiler.Emit;

namespace UdonSharpOptimizer.Passes
{
    // Initializes the context with information from the module
    // Gathers all jump labels, return addresses, and switch tables
    // Marks addresses that can be jumped to
    internal sealed class InitializeContextPass : IOptimizerPass
    {
        public void Execute(OptimizerContext context)
        {
            AssemblyModule assemblyModule = context.AssemblyModule;

            // Copy instructions out of module
            for (int i = 0; i < assemblyModule.InstructionCount; i++)
            {
                AssemblyInstruction inst = assemblyModule[i];
                context.Instrs.Add(inst);
                if (inst is JumpInstruction jInst)
                {
                    context.JumpLabels.Add(jInst.JumpTarget);
                }
                else if (inst is JumpIfFalseInstruction jifInst)
                {
                    context.JumpLabels.Add(jifInst.JumpTarget);
                }
                else if (inst is PushInstruction pInst && pInst.PushValue.Flags == Value.ValueFlags.InternalGlobal)
                {
                    // TODO: Hacky, make a more proper way to do this
                    if (pInst.PushValue.DefaultValue is uint && pInst.PushValue.UniqueID.StartsWith("__gintnl_RetAddress_", StringComparison.Ordinal))
                    {
                        context.AddrValues.Add(pInst.PushValue);
                    }
                    else if (pInst.PushValue.DefaultValue is uint[] && pInst.PushValue.UniqueID.StartsWith("__gintnl_SwitchTable_", StringComparison.Ordinal))
                    {
                        context.SwitchTables.Add(pInst.PushValue);
                    }
                }
            }

            // Make a set of instructions that can be jumped to
            List<uint> jumpAddress = new List<uint>();
            foreach (JumpLabel jumpLabel in context.JumpLabels)
            {
                jumpAddress.Add(jumpLabel.Address);
            }
            foreach (Value switchTable in context.SwitchTables)
            {
                jumpAddress.AddRange((uint[])switchTable.DefaultValue);
            }
            jumpAddress.Sort((a, b) => a.CompareTo(b));
            List<uint>.Enumerator jumpEnumerator = jumpAddress.GetEnumerator();
            if (jumpEnumerator.MoveNext())
            {
                foreach (AssemblyInstruction inst in context.Instrs)
                {
                    if (inst.Size != 0)
                    {
                        context.InstrMap.Add(inst.InstructionAddress, inst);
                        if (inst.InstructionAddress == jumpEnumerator.Current)
                        {
                            context.HasJumpSet.Add(inst);
                            while (inst.InstructionAddress == jumpEnumerator.Current)
                            {
                                if (!jumpEnumerator.MoveNext())
                                {
                                    return;
                                }
                            }
                        }
                    }

                    if (inst.InstructionAddress > jumpEnumerator.Current)
                    {
                        // Shouldn't happen but just in case
                        throw new OptimizerAbortException("Jump Target Set Desync");
                    }
                }
            }
        }
    }
}