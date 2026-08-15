/*
 * Unofficial UdonSharp Optimizer
 * Written by BlueAmulet
 */

using System;

namespace UdonSharpOptimizer
{
    // Aborts optimization incase of issues
    internal sealed class OptimizerAbortException : Exception
    {
        public OptimizerAbortException(string reason) : base(reason)
        {
        }
    }
}
