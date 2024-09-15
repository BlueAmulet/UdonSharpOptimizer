# UdonSharpOptimizer
[VCC Listing](https://blueamulet.github.io/UdonSharpOptimizer/)  
Adds a hook to UdonSharp to process the generated Udon, reducing the number of instructions and making better use of temporary variables, as well as fixing a bug resulting in unnecessary variables. This results in udon programs that are faster and smaller.

## Optimizations
There are currently 3 class of optimizations:
### COPY Removal:
UdonSharp likes to copy values into variables that only get used once or never at all, we eliminate these COPY instructions by modifying other instructions to store directly to the copy's target, or load directly from the copy's source.
### Tail Call Optimization:
Any call to another Udon function followed by a return, can have its setup and the corresponding return removed, utilizing the return instructions of the called Udon function instead.
### Variable Reduction:
UdonSharp makes a *LOT* of temporary variables. We detect places where we can reuse existing temporary variables instead of creating new ones. This does not make the program faster but does make the program smaller.

## Changelog
1.0.0 - Initial 2022 version  
1.0.1 - 2024 Update  
1.0.2 - Fixed switch statements  
1.0.3 - Reduced number of variables  
1.0.4 - __this_ fix for even less variables  
1.0.5 - Added ExternWrite+Copy check for variables, added missing jump checks  
1.0.6 - Added tail call optimization  
1.0.7 - Single .unitypackage installation  
