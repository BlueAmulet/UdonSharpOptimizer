using System.Collections.Generic;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Assembly.Instructions;

namespace UdonSharpOptimizer.Optimizations
{
    internal class OPTCopyLoad : IBaseOptimization
    {
        public bool Enabled()
        {
            return OptimizerSettings.Instance.CopyAndLoad;
        }

        public void ProcessInstruction(Optimizer optimizer, List<AssemblyInstruction> instrs, int i)
        {
            // Remove Copy: Copy + JumpIf
            if (instrs[i] is CopyInstruction cInst && instrs[i + 1] is PushInstruction pInst)
            {
                if (Optimizer.IsPrivate(cInst.TargetValue) && cInst.TargetValue.UniqueID == pInst.PushValue.UniqueID && !optimizer.HasJump(pInst) && !optimizer.ReadScan(n => n == i + 1, cInst.TargetValue))
                {
                    instrs[i] = optimizer.TransferInstr(Optimizer.CopyComment("OPTCopyLoad", cInst), cInst);
                    instrs[i + 1] = optimizer.TransferInstr(new PushInstruction(cInst.SourceValue), pInst);
                    optimizer.removedInsts += 3; // PUSH, PUSH, COPY
                }
            }
        }
    }
}
