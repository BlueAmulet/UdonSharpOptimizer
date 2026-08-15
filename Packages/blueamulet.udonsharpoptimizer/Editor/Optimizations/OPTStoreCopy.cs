/*
 * Unofficial UdonSharp Optimizer
 * Written by BlueAmulet
 */

using System.Collections.Generic;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Assembly.Instructions;

namespace UdonSharpOptimizer.Optimizations
{
    internal sealed class OPTStoreCopy : BaseOptimization
    {
        protected override string GUILabel => "Store Copy";

        public override bool Enabled => OptimizerSettings.Instance.StoreAndCopy;

        public override void ProcessInstruction(OptimizerContext context, IList<AssemblyInstruction> instrs, int i)
        {
            // Remove Copy: Extern + Copy
            if (instrs[i] is PushInstruction pInst
                && i < instrs.Count - 2
                && Optimizer.IsExternWrite(instrs[i + 1])
                && instrs[i + 2] is CopyInstruction cInst
                && Optimizer.IsPrivate(pInst.PushValue)
                && pInst.PushValue.UniqueID == cInst.SourceValue.UniqueID
                && !context.HasJump(i + 1, i + 2)
                && !context.ReadScan(n => n == i || n == i + 2, pInst.PushValue))
            {
                instrs[i] = context.TransferInstr(new PushInstruction(cInst.TargetValue), pInst);
                instrs[i + 2] = context.TransferInstr(CopyComment("OPTStoreCopy", cInst), cInst);
                CountRemoved(context, 3); // PUSH, PUSH, COPY
            }
        }
    }
}
