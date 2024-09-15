using Microsoft.CodeAnalysis;
using System;

namespace UdonSharpOptimizer
{
    [Generator]
    public class AutoDefine : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            if (context.Compilation.AssemblyName == "UdonSharp.Editor")
            {
                Console.WriteLine("[USOPatch] Exposing UdonSharp internals to Optimizer");
                context.AddSource("USOPatch", "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"BlueAmulet.UdonSharpOptimizer\")]");
            }
        }

        public void Initialize(GeneratorInitializationContext context) { }
    }
}