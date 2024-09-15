using System.Collections.Generic;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Assembly.Instructions;

namespace UdonSharpOptimizer.Optimizations
{
    internal class OPTCopyTest : IBaseOptimization
    {
        public bool Enabled()
        {
            return OptimizerSettings.Instance.CopyAndTest;
        }

        public void ProcessInstruction(Optimizer optimizer, List<AssemblyInstruction> instrs, int i)
        {
            // Remove Copy: Copy + JumpIf
            if (instrs[i] is CopyInstruction cInst && i < instrs.Count - 1 && instrs[i + 1] is JumpIfFalseInstruction jifInst)
            {
                if (Optimizer.IsPrivate(cInst.TargetValue) && cInst.TargetValue.UniqueID == jifInst.ConditionValue.UniqueID && !optimizer.HasJump(jifInst) && !optimizer.ReadScan(n => n == i + 1, cInst.TargetValue))
                {
                    instrs[i] = optimizer.TransferInstr(Optimizer.CopyComment("OPTCopyTest", cInst), cInst);
                    instrs[i + 1] = optimizer.TransferInstr(new JumpIfFalseInstruction(jifInst.JumpTarget, cInst.SourceValue), jifInst);
                    optimizer.removedInsts += 3; // PUSH, PUSH, COPY
                }
            }
        }
    }
}
