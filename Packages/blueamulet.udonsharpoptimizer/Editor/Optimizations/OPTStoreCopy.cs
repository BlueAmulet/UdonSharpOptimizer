using System.Collections.Generic;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Assembly.Instructions;

namespace UdonSharpOptimizer.Optimizations
{
    internal class OPTStoreCopy : IBaseOptimization
    {
        public bool Enabled()
        {
            return OptimizerSettings.Instance.StoreAndCopy;
        }

        public void ProcessInstruction(Optimizer optimizer, List<AssemblyInstruction> instrs, int i)
        {
            // Remove Copy: Extern + Copy
            if (instrs[i] is PushInstruction pInst && i < instrs.Count - 2 && Optimizer.IsExternWrite(instrs[i + 1]) && instrs[i + 2] is CopyInstruction cInst)
            {
                if (Optimizer.IsPrivate(pInst.PushValue) && pInst.PushValue.UniqueID == cInst.SourceValue.UniqueID && !optimizer.HasJump(i + 1, i + 2) && !optimizer.ReadScan(n => n == i || n == i + 2, pInst.PushValue))
                {
                    instrs[i] = optimizer.TransferInstr(new PushInstruction(cInst.TargetValue), pInst);
                    instrs[i + 2] = optimizer.TransferInstr(Optimizer.CopyComment("OPTStoreCopy", cInst), cInst);
                    optimizer.removedInsts += 3; // PUSH, PUSH, COPY
                }
            }
        }
    }
}
