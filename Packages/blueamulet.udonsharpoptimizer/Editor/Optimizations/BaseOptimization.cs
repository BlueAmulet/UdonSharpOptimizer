using System.Collections.Generic;
using System.Threading;
using UdonSharp.Compiler.Assembly;
using UnityEditor;

namespace UdonSharpOptimizer.Optimizations
{
    abstract class BaseOptimization : IBaseOptimization
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

        public abstract void ProcessInstruction(Optimizer optimizer, List<AssemblyInstruction> instrs, int i);

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

        protected void CountRemoved(Optimizer optimizer, int count)
        {
            optimizer.removedInstrs += count;
            Interlocked.Add(ref removedInstructions, count);
        }
    }
}
