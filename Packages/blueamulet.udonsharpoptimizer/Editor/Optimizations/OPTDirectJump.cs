/*
 * Unofficial UdonSharp Optimizer
 * Written by BlueAmulet
 */

using System.Collections.Generic;
using System.Threading;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Assembly.Instructions;
using UnityEditor;

namespace UdonSharpOptimizer.Optimizations
{
    internal sealed class OPTDirectJump : IInstructionPass
    {
        private readonly string _statsKey = OptimizerStats.KeyFor(typeof(OPTDirectJump));
        private int patchedInstructions;

        public OPTDirectJump()
        {
            patchedInstructions = OptimizerStats.Load(_statsKey);
        }

        public bool Enabled => OptimizerSettings.Instance.DirectJump;

        public void ResetStats()
        {
            patchedInstructions = 0;
        }

        public void SaveStats()
        {
            OptimizerStats.Save(_statsKey, patchedInstructions);
        }

        public void OnGUI()
        {
            OptimizerEditorWindow.AlignedText("Direct Jump", patchedInstructions.ToString(), EditorStyles.label);
        }

        public void ProcessInstruction(OptimizerContext context, IList<AssemblyInstruction> instrs, int i)
        {
            // Simplify jump chains
            if (instrs[i] is JumpInstruction jInst)
            {
                JumpLabel innerJump = jInst.JumpTarget;
                int chain = 0;
                while (context.InstrMap[innerJump.Address] is JumpInstruction nextJump)
                {
                    innerJump = nextJump.JumpTarget;
                    chain++;
                }
                if (innerJump.Address != jInst.JumpTarget.Address)
                {
                    instrs[i] = context.TransferInstr(new JumpInstruction(innerJump), jInst);
                    Comment comment = new Comment($"OPTDirectJump: Skipped {chain} jumps");
                    comment.InstructionAddress = instrs[i].InstructionAddress;
                    instrs.Insert(i, comment);
                    Interlocked.Add(ref patchedInstructions, chain);
                }
            }
            else if (instrs[i] is JumpIfFalseInstruction jifInst)
            {
                JumpLabel innerJump = jifInst.JumpTarget;
                int chain = 0;
                while (context.InstrMap[innerJump.Address] is JumpInstruction nextJump)
                {
                    innerJump = nextJump.JumpTarget;
                    chain++;
                }
                if (innerJump.Address != jifInst.JumpTarget.Address)
                {
                    instrs[i] = context.TransferInstr(new JumpIfFalseInstruction(innerJump, jifInst.ConditionValue), jifInst);
                    Comment comment = new Comment($"OPTDirectJump: Skipped {chain} jumps");
                    comment.InstructionAddress = instrs[i].InstructionAddress;
                    instrs.Insert(i, comment);
                    Interlocked.Add(ref patchedInstructions, chain);
                }
            }
        }
    }
}
