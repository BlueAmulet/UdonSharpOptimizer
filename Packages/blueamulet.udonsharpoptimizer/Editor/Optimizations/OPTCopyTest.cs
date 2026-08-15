/*
 * Unofficial UdonSharp Optimizer
 * Written by BlueAmulet
 */

using System.Collections.Generic;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Assembly.Instructions;

namespace UdonSharpOptimizer.Optimizations
{
    internal sealed class OPTCopyTest : BaseOptimization
    {
        protected override string GUILabel => "Copy Test";

        public override bool Enabled => OptimizerSettings.Instance.CopyAndTest;

        public override void ProcessInstruction(OptimizerContext context, IList<AssemblyInstruction> instrs, int i)
        {
            // Remove Copy: Copy + JumpIf
            if (instrs[i] is CopyInstruction cInst
                && i < instrs.Count - 1
                && instrs[i + 1] is JumpIfFalseInstruction jifInst
                && Optimizer.IsPrivate(cInst.TargetValue)
                && cInst.TargetValue.UniqueID == jifInst.ConditionValue.UniqueID
                && !context.HasJump(jifInst)
                && !context.ReadScan(n => n == i + 1, cInst.TargetValue))
            {
                instrs[i] = context.TransferInstr(CopyComment("OPTCopyTest", cInst), cInst);
                instrs[i + 1] = context.TransferInstr(new JumpIfFalseInstruction(jifInst.JumpTarget, cInst.SourceValue), jifInst);
                CountRemoved(context, 3); // PUSH, PUSH, COPY
            }
        }
    }
}
