namespace PetScale.Enums;

#pragma warning disable MA0048
/// <summary>
///     Pet exd rows corresponding to summoner pets with scaling
/// </summary>
/// <remarks>
///     Important that value name is identical with <see cref="SummonModel"/> values to map name and scales of pet
/// </remarks>
public enum SummonRow : uint
{
    Bahamut = 10,
    Phoenix = 14,
    Ifrit = 30,
    Titan = 31,
    Garuda = 32,
}

/// <summary>
///     Since there's no good way of identifying BNPCs in game without using an external source, this is a direct mapping of Character.CharacterData.ModelCharaId
/// </summary>
/// <remarks>
///     Important that value name is identical with <see cref="SummonModel"/> values to map name and scales of pet
/// </remarks>
public enum SummonModel
{
    AllSummons = 0,
    Bahamut = 1930,
    Phoenix = 2620,
    Ifrit = 3122,
    Titan = 3124,
    Garuda = 3123,
}

public enum SummonSize
{
    SmallModelScale,
    MediumModelScale,
    LargeModelScale,
}
