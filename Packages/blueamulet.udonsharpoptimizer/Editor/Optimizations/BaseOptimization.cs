/*
 * Unofficial UdonSharp Optimizer
 * Written by BlueAmulet
 */

using System.Collections.Generic;
using System.Threading;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Assembly.Instructions;
using UnityEditor;

namespace UdonSharpOptimizer.Optimizations
{
    internal abstract class BaseOptimization : IInstructionPass
    {
        private readonly string _statsKey;
        private int removedInstructions;
        protected abstract string GUILabel { get; }
        public abstract bool Enabled { get; }

        protected BaseOptimization()
        {
            _statsKey = OptimizerStats.KeyFor(GetType());
            removedInstructions = OptimizerStats.Load(_statsKey);
        }

        public abstract void ProcessInstruction(OptimizerContext context, IList<AssemblyInstruction> instrs, int i);

        public void ResetStats()
        {
            removedInstructions = 0;
        }

        public void SaveStats()
        {
            OptimizerStats.Save(_statsKey, removedInstructions);
        }

        public void OnGUI()
        {
            OptimizerEditorWindow.AlignedText(GUILabel, removedInstructions.ToString(), EditorStyles.label);
        }

        protected void CountRemoved(OptimizerContext context, int count)
        {
            context.RemovedInstrs += count;
            Interlocked.Add(ref removedInstructions, count);
        }

        internal static Comment CopyComment(string code, CopyInstruction cInst)
        {
            return new Comment($"{code}: Removed {cInst.SourceValue.UniqueID} => {cInst.TargetValue.UniqueID} copy");
        }
    }
}
