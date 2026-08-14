using System.Collections.Generic;
using UdonSharp.Compiler.Assembly;
using UnityEditor;

namespace UdonSharpOptimizer.Optimizations
{
    abstract class BaseOptimization : IBaseOptimization
    {
        private int removedInstructions;
        protected abstract string GUILabel { get; }
        public abstract bool Enabled { get; }

        public abstract void ProcessInstruction(Optimizer optimizer, List<AssemblyInstruction> instrs, int i);

        public void ResetStats()
        {
            removedInstructions = 0;
        }

        public void OnGUI()
        {
            OptimizerEditorWindow.AlignedText(GUILabel, removedInstructions.ToString(), EditorStyles.label);
        }

        protected void CountRemoved(Optimizer optimizer, int count)
        {
            optimizer.removedInstrs += count;
            removedInstructions += count;
        }
    }
}
