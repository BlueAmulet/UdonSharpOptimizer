/*
 * Unofficial UdonSharp Optimizer
 * Written by BlueAmulet
 */

using System.Collections.Generic;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Assembly.Instructions;

namespace UdonSharpOptimizer.Optimizations
{
    internal sealed class OPTDoubleCopy : BaseOptimization
    {
        protected override string GUILabel => "Double Copy";

        public override bool Enabled => OptimizerSettings.Instance.DoubleCopy;

        public override void ProcessInstruction(OptimizerContext context, IList<AssemblyInstruction> instrs, int i)
        {
            // Remove Copy: Copy + Copy
            if (instrs[i] is CopyInstruction cInst1
                && i < instrs.Count - 1
                && instrs[i + 1] is CopyInstruction cInst2
                && Optimizer.IsPrivate(cInst1.TargetValue)
                && cInst1.TargetValue.UniqueID == cInst2.SourceValue.UniqueID
                && !context.HasJump(cInst2)
                && !context.ReadScan(n => n == i + 1, cInst1.TargetValue))
            {
                instrs[i] = context.TransferInstr(CopyComment("OPTDoubleCopy", cInst1), cInst1);
                instrs[i + 1] = context.TransferInstr(new CopyInstruction(cInst1.SourceValue, cInst2.TargetValue), cInst2);
                CountRemoved(context, 3); // PUSH, PUSH, COPY
            }
        }
    }
}
