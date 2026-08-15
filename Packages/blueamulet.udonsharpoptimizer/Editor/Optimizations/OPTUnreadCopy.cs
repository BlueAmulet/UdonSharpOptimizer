/*
 * Unofficial UdonSharp Optimizer
 * Written by BlueAmulet
 */

using System.Collections.Generic;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Assembly.Instructions;

namespace UdonSharpOptimizer.Optimizations
{
    internal sealed class OPTUnreadCopy : BaseOptimization
    {
        protected override string GUILabel => "Unread Copy";

        public override bool Enabled => OptimizerSettings.Instance.CleanUnreadCopy;

        public override void ProcessInstruction(OptimizerContext context, IList<AssemblyInstruction> instrs, int i)
        {
            // Remove Copy: Unread target (Cleans up Cow dirty)
            if (instrs[i] is CopyInstruction cInst
                && Optimizer.IsPrivate(cInst.TargetValue)
                && !context.ReadScan(_ => false, cInst.TargetValue))
            {
                instrs[i] = context.TransferInstr(CopyComment("OPTUnreadCopy", cInst), cInst);
                CountRemoved(context, 3); // PUSH, PUSH, COPY
            }
        }
    }
}
