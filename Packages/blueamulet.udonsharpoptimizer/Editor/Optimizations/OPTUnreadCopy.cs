using System.Collections.Generic;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Assembly.Instructions;

namespace UdonSharpOptimizer.Optimizations
{
    internal class OPTUnreadCopy : IBaseOptimization
    {
        public bool Enabled()
        {
            return OptimizerSettings.Instance.CleanUnreadCopy;
        }

        public void ProcessInstruction(Optimizer optimizer, List<AssemblyInstruction> instrs, int i)
        {
            // Remove Copy: Unread target (Cleans up Cow dirty)
            if (instrs[i] is CopyInstruction cInst)
            {
                if (Optimizer.IsPrivate(cInst.TargetValue) && !optimizer.ReadScan(_ => false, cInst.TargetValue))
                {
                    instrs[i] = optimizer.TransferInstr(Optimizer.CopyComment("OPTUnreadCopy", cInst), cInst);
                    optimizer.removedInsts += 3; // PUSH, PUSH, COPY
                }
            }
        }
    }
}
