This is an experimental optimizer for UdonSharp to remove unnecessary instructions and variables, resulting in smaller and faster code.

After any UdonSharp compile, a line will appear in the console similar to the following:
[Optimizer] Removed # instructions, # variables, and # extra __this total

For Unity 2022, no permanent changes are made to the VRCSDK, all changes are made in memory and can be easily removed by removing this package.
The USOPatch.dll included is part of the non permanent change system, allowing the optimizer access to UdonSharp's internals.
The source code for this dll is included in the USOPatch folder.

For Unity 2019, an additional file is written to the VRCSDK to allow the optimizer to function.
This can be found at Packages/com.vrchat.worlds/Integrations/UdonSharp/Editor/USOInternals.cs

Changelog - 1.1.1:
* Expanded store load detection to consider JumpIfFalse instructions
* Variable reduction counter now reports the correct difference:
  * UdonSharp emits multiple Values referring to the same variable
  * The Optimizer would incorrectly count each instance of the variable
  * The Optimizer would also incorrectly count variables as removed if one copy still remained
* Untangled the store load detection from the block remapping pass