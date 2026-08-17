## 1.1.1
### Features
* Expanded store load detection to consider JumpIfFalse instructions

### Bug Fixes
* Variable reduction counter now reports the correct difference:
  * UdonSharp emits multiple Values referring to the same variable
  * The Optimizer would incorrectly count each instance of the variable
  * The Optimizer would also incorrectly count variables as removed if one copy still remained

### Changes
* Untangled the store load detection from the block remapping pass

## 1.1.0
### Changes
* Consolidated Optimizer state into a single context class
* Refactored the Optimizer into separate files of individual passes
* Version number is now shown in Settings panel

## 1.0.14
### Features
* Last Build statistics now persist across domain reloads

### Bug Fixes
* Added `SendCustomNetworkEvent` to possible reentrancy list
* Fixed multithreaded issues with Last Build statistics
* Ensure consistent static initialization with Optimizer

### Changes
* Settings panel now indicated Optimizer is inactive when disabled to avoid confusion
  * It is still hooked into UdonSharp, but does not process programs

## 1.0.13
### Bug Fixes
* Fixed a long standing bug causing internal state corruption in udon programs
  * The Optimizer did not consider possible reentrancy from another udon program
  * If invoking a function on another program would call back into the original program, internal state could end up corrupted
  * Mark calls to `SendCustomEvent` and `SetProgramVariable` (FieldChangeCallback) as block boundaries to prevent variables from being shared across the call point
  * Huge thanks to @BobyStar for helping me diagnose this issue

### Changes
* Minor code cleanup
* Use StringComparison.Ordinal for locale independant comparisons
* Fixed missing BOM in some files

## 1.0.12
### Features
* Added new Direct Jump chain optimization
  * Jump instructions pointing to other Jump instructions are now optimized
* Tail Call optimization now considers function calls followed by a jump to a return instruction

## 1.0.11
### Features
* The Settings panel now shows individual statistics per optimization
* Tail Call optimization now supports partial optimization when the return is across a jump boundary

## Bug Fixes
* Unity 2019 support has been fixed
  * The extra `USOInternals.cs` file is required on this version, as Unity 2019 does not support source generators
  * A pop up is used to allow the user to confirm if they want to make changes to the VRCSDK

## 1.0.10
### Features
* Added new optimization targeting Copy instructions to another Copy
* Improved block based variable reduction
  * The last usage of a variable is now tracked
  * The counter corresponding to that variable is freed after the last usage, allowing it to be reused in the block

### Changes
* Refactored optimizations to be in separate files
* Moved harmony patches to the injection class
* Optimizations are now by name and no longer have an assigned number

## 1.0.9b
### Bug Fixes
* Calls to other functions are now considered block boundaries
* This prevents functions from corrupting the internal state of other functions

## 1.0.9
### Features
* Added in a new block based variable reduction system
 * Programs are separated into blocks based on jump targets
 * All temporary variables that reside within a single block can be optimized into a shared temporary

### Changes
* Moved Tail Call optimization into the first pass with other optimizations
* Fixed `Unoffical` typo

## 1.0.8
### Features
* Added a basic settings panel allowing each optimization to be turned off
* Also displays injection status and optimization statistics

## 1.0.7
### Features
* First GitHub release, so Changelog is reconstructed
* Added source generator to allow Optimizer to access UdonSharp internals non destructively

### Changes
* Removed the old "destructive" patch that would create a `USOInternals.cs` file in the VRCSDK

## 1.0.6
### Features
* Added Tail Call optimization
 * Calls to other functions followed by a return can have both the return address push and the return sequence removed

## 1.0.5
### Features
* Added new optimization targeting Extern that return a value followed by a Copy

### Bug Fixes
* Added missing jump label checks to optimizations

## 1.0.4
### Features
* Remove excessive __this variables
 * __this is a special type of variable that refers to the udon program or the object it's attached to
 * It always provides either the UdonBehaviour, the GameObject, or the Transform

## 1.0.3
### Features
* Added variable reduction pass
 * Any variable that is only used as a storage location to be immediately loaded can be optimized into a shared temporary

## 1.0.2
### Bug Fixes
* The Optimizer now processes and fixes switch tables
 * A new hook was added for switch table values to allow the Optimizer to identify them
 * The addresses inside are now updated to correspond with instruction changes

## 1.0.1
### History
* Resumed work on the Optimizer again in 2024

## 1.0.0
### History
* Initial beginnings of the Optimizer in 2022
