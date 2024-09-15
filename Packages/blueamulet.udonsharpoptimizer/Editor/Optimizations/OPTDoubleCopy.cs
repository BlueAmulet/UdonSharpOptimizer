using System.Collections.Generic;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Assembly.Instructions;

namespace UdonSharpOptimizer.Optimizations
{
    internal class OPTDoubleCopy : IBaseOptimization
    {
        public bool Enabled()
        {
            return OptimizerSettings.Instance.DoubleCopy;
        }

        public void ProcessInstruction(Optimizer optimizer, List<AssemblyInstruction> instrs, int i)
        {
            // Remove Copy: Copy + Copy
            if (instrs[i] is CopyInstruction cInst1 && i < instrs.Count - 1 && instrs[i + 1] is CopyInstruction cInst2)
            {
                if (Optimizer.IsPrivate(cInst1.TargetValue) && cInst1.TargetValue.UniqueID == cInst2.SourceValue.UniqueID && !optimizer.HasJump(cInst2) && !optimizer.ReadScan(n => n == i + 1, cInst1.TargetValue))
                {
                    instrs[i] = optimizer.TransferInstr(Optimizer.CopyComment("OPTDoubleCopy", cInst1), cInst1);
                    instrs[i + 1] = optimizer.TransferInstr(new CopyInstruction(cInst1.SourceValue, cInst2.TargetValue), cInst2);
                    optimizer.removedInsts += 3; // PUSH, PUSH, COPY
                }
            }
        }
    }
}
