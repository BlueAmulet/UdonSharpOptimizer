/*
 * Unofficial UdonSharp Optimizer
 * Written by BlueAmulet
 */

using System.Collections.Generic;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Assembly.Instructions;

namespace UdonSharpOptimizer.Optimizations
{
    internal sealed class OPTCopyLoad : BaseOptimization
    {
        protected override string GUILabel => "Copy Load";

        public override bool Enabled => OptimizerSettings.Instance.CopyAndLoad;

        public override void ProcessInstruction(OptimizerContext context, IList<AssemblyInstruction> instrs, int i)
        {
            // Remove Copy: Copy + Push
            if (instrs[i] is CopyInstruction cInst
                && i < instrs.Count - 1
                && instrs[i + 1] is PushInstruction pInst
                && Optimizer.IsPrivate(cInst.TargetValue)
                && cInst.TargetValue.UniqueID == pInst.PushValue.UniqueID
                && !context.HasJump(pInst)
                && !context.ReadScan(n => n == i + 1, cInst.TargetValue))
            {
                instrs[i] = context.TransferInstr(CopyComment("OPTCopyLoad", cInst), cInst);
                instrs[i + 1] = context.TransferInstr(new PushInstruction(cInst.SourceValue), pInst);
                CountRemoved(context, 3); // PUSH, PUSH, COPY
            }
        }
    }
}
