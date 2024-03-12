// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Design", "MA0049:Type name should not match containing namespace", Justification = "...and I took that personally.", Scope = "type", Target = "~T:PetScale.PetScale")]
[assembly: SuppressMessage("Design", "MA0038:Make method static (deprecated, use CA1822 instead)", Justification = "Obsolete", Scope = "member", Target = "~M:PetScale.Helpers.Utilities.SetScale(FFXIVClientStructs.FFXIV.Client.Game.Character.BattleChara*,System.Single)")]
[assembly: SuppressMessage("Design", "MA0041:Make property static (deprecated, use CA1822 instead)", Justification = "Obsolete", Scope = "member", Target = "~P:PetScale.PetScale.BattleCharaSpan")]
[assembly: SuppressMessage("Design", "MA0016:Prefer using collection abstraction instead of implementation", Justification = "Not meant for public consumption", Scope = "member", Target = "~P:PetScale.Windows.ConfigWindow.petMap")]
[assembly: SuppressMessage("Performance", "MA0066:Hash table unfriendly type is used in a hash table", Justification = "None", Scope = "member", Target = "~F:PetScale.PetScale.activePetDictionary")]
[assembly: SuppressMessage("Design", "MA0038:Make method static (deprecated, use CA1822 instead)", Justification = "Obsolete", Scope = "member", Target = "~M:PetScale.Helpers.Utilities.CachePlayerList(System.UInt32,System.Collections.Generic.Queue{System.String},System.Span{FFXIVClientStructs.Interop.Pointer{FFXIVClientStructs.FFXIV.Client.Game.Character.BattleChara}})")]
[assembly: SuppressMessage("Design", "MA0038:Make method static (deprecated, use CA1822 instead)", Justification = "Obsolete", Scope = "member", Target = "~M:PetScale.Helpers.Utilities.PetVisible(FFXIVClientStructs.FFXIV.Client.Game.Character.BattleChara*)~System.Boolean")]
