// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Design", "MA0049:Type name should not match containing namespace", Justification = "...and I took that personally.", Scope = "type", Target = "~T:PetScale.PetScale")]
[assembly: SuppressMessage("Design", "MA0041:Make property static (deprecated, use CA1822 instead)", Justification = "Obsolete", Scope = "member", Target = "~P:PetScale.PetScale.BattleCharaSpan")]
[assembly: SuppressMessage("Performance", "MA0066:Hash table unfriendly type is used in a hash table", Justification = "None", Scope = "member", Target = "~F:PetScale.PetScale.activePetDictionary")]
[assembly: SuppressMessage("Design", "MA0038:Make method static (deprecated, use CA1822 instead)", Justification = "Obsolete", Scope = "member", Target = "~M:PetScale.Helpers.Utilities.PetVisible(FFXIVClientStructs.FFXIV.Client.Game.Character.BattleChara*)~System.Boolean")]
[assembly: SuppressMessage("Minor Code Smell", "S3267:Loops should be simplified with \"LINQ\" expressions", Justification = "Can't select pointers", Scope = "member", Target = "~M:PetScale.PetScale.CheckFairies(FFXIVClientStructs.FFXIV.Client.Game.Character.BattleChara*)")]
[assembly: SuppressMessage("Design", "MA0048:File name must match type name", Justification = "No", Scope = "type", Target = "~T:PetScale.Helpers.ReadOnlySeStringExtension")]
