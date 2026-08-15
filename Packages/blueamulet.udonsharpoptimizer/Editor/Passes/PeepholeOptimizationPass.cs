/*
 * Unofficial UdonSharp Optimizer
 * Written by BlueAmulet
 */

using System.Collections.Generic;
using UdonSharp.Compiler.Assembly;
using UdonSharpOptimizer.Optimizations;

namespace UdonSharpOptimizer.Passes
{
    // Handles running all per instruction passes
    internal sealed class PeepholeOptimizationPass : IOptimizerPass
    {
        private readonly IInstructionPass[] _optimizations;

        public PeepholeOptimizationPass(IInstructionPass[] optimizations)
        {
            _optimizations = optimizations;
        }

        public void Execute(OptimizerContext context)
        {
            // Determine which optimization passes are active
            List<IInstructionPass> activeOptList = new List<IInstructionPass>();
            foreach (IInstructionPass optimization in _optimizations)
            {
                if (optimization.Enabled)
                {
                    activeOptList.Add(optimization);
                }
            }
            IInstructionPass[] activeOptimizations = activeOptList.ToArray();

            // Apply each optimization
            IList<AssemblyInstruction> instrs = context.Instrs;
            for (int i = 0; i < instrs.Count; i++)
            {
                foreach (IInstructionPass optimization in activeOptimizations)
                {
                    optimization.ProcessInstruction(context, instrs, i);
                }
            }
        }
    }
}