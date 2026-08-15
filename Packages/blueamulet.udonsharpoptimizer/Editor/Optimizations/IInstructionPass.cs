/*
 * Unofficial UdonSharp Optimizer
 * Written by BlueAmulet
 */

using System.Collections.Generic;
using UdonSharp.Compiler.Assembly;

namespace UdonSharpOptimizer.Optimizations
{
    // Interface for any per instruction optimization pass
    internal interface IInstructionPass
    {
        bool Enabled { get; }

        void ResetStats();

        void SaveStats();

        void OnGUI();

        void ProcessInstruction(OptimizerContext context, IList<AssemblyInstruction> instrs, int i);
    }
}
