using System.Collections.Generic;
using UdonSharp.Compiler.Assembly;

namespace UdonSharpOptimizer.Optimizations
{
    internal interface IBaseOptimization
    {
        bool Enabled();

        void ProcessInstruction(Optimizer optimizer, List<AssemblyInstruction> instrs, int i);
    }
}
