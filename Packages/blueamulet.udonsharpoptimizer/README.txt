This is an experimental optimizer for UdonSharp to remove unnecessary instructions and variables, resulting in smaller and faster code.

After any UdonSharp compile, a line will appear in the console similar to the following:
[Optimizer] Removed # instructions, # variables, and # extra __this total

No permanent changes are made to the VRCSDK, all changes are made in memory and can be easily removed by removing this package.

The USOPatch.dll included is part of the non permanent change system, allowing the optimizer access to UdonSharp's internals. The source code for this dll is included in the USOPatch folder

Changelog:
1.0.0  - Initial 2022 version
1.0.1  - 2024 Update
1.0.2  - Fixed switch statements
1.0.3  - Reduced number of variables
1.0.4  - __this_ fix for even less variables
1.0.5  - Added ExternWrite+Copy check for variables, added missing jump checks
1.0.6  - Added tail call optimization
1.0.7  - Single .unitypackage installation
1.0.8  - Added basic Settings panel
1.0.9  - Moved TCO into first pass, added block based variable reduction
1.0.9b - Fixed udon functions destroying variables in other functions
1.0.10 - Code refactor, added additional instruction and variable optimizations