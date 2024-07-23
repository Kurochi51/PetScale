namespace PetScale.Enums;

#pragma warning disable MA0048
/// <summary>
///     Pet exd rows corresponding to summoner pets with scaling
/// </summary>
public enum PetRow : uint
{
    Bahamut = 10,
    Phoenix = 14,
    Ifrit = 30,
    Titan = 31,
    Garuda = 32,
    SolarBahamut = 46,
    // SCH Fairies
    Eos = 6,
    Selene = 7,
    Seraph = 15,
    // MCH Things
    Rook = 8,
    AutomatonQueen = 18,
    // DRK Emo Clone
    Esteem = 17,
    // Custom SMN pets
    Carbuncle = 23,
    RubyCarbuncle = 24,
    TopazCarbuncle = 25,
    EmeraldCarbuncle = 26,
    IfritEgi = 27,
    TitanEgi = 28,
    GarudaEgi = 29,
}

/// <summary>
///     Since there's no good way of identifying BNPCs in game without using an external source, this is a direct mapping of Character.CharacterData.ModelCharaId
/// </summary>
public enum PetModel
{
    AllPets = 0,
    Bahamut = 1930,
    Phoenix = 2620,
    Ifrit = 3122,
    Titan = 3124,
    Garuda = 3123,
    SolarBahamut = 4038,
    // SCH Fairies
    Eos = 407,
    Selene = 408,
    Seraph = 2619,
    // MCH Things
    Rook = 1027,
    AutomatonQueen = 2618,
    // DRK Emo Clone
    Esteem = 2621,
    // Custom SMN pets
    Carbuncle = 411,
    RubyCarbuncle = 410,
    TopazCarbuncle = 412,
    EmeraldCarbuncle = 409,
    IfritEgi = 415,
    TitanEgi = 416,
    GarudaEgi = 417,
}

public enum PetSize
{
    SmallModelScale,
    MediumModelScale,
    LargeModelScale,
    Custom,
}

public enum PetState
{
    Off,
    Self,
    Others,
    All,
}
