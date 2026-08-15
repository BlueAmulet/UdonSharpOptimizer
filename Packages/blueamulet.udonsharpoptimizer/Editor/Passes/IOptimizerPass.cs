/*
 * Unofficial UdonSharp Optimizer
 * Written by BlueAmulet
 */

namespace UdonSharpOptimizer.Passes
{
    // Generic interface for an optimizer pass
    internal interface IOptimizerPass
    {
        void Execute(OptimizerContext context);
    }
}
