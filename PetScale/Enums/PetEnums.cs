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

    // BST Pets 1-50
    CuSith = 47,
    Squirrel = 48,
    Lamb = 49,
    pugil = 50,
    Opo_Opo = 52,
    Dodo = 53,
    Coblyn = 54,
    Diremite = 55,
    Megalocrab = 56,
    Wespe = 57,
    Vulture = 58,
    Mandragora = 59,
    Geshunpest = 60,
    Puk = 61,
    Crab = 62,
    Mantis = 63,
    Slime = 64,
    Dullahan = 65,
    Bat = 66,
    Flying_Trap = 67,
    Ziz = 68,
    Sabotender = 69,
    Golem = 70,
    Apkallu = 71,
    Adamantoise = 72,
    Buffalo = 73,
    Uragnite = 74,
    Worm = 75,
    Spriggan = 76,
    Goobbue = 77,
    Gigantoad = 78,
    Colibri = 79,
    Coeurl = 80,
    Raptor = 81,
    Drake = 82,
    Treant = 83,
    Antling = 84,
    Chimera = 85,
    Morbol = 86,
    Ghost = 87,
    Salamander = 88,
    Cobra = 89,
    Hydra = 90,
    Damselfly = 91,
    Rotting_Goobbue = 92,
    Zu = 93,
    Ice_Golem = 94,
    Karlabos = 95,
    Rafflesia = 96,
    Behemoth = 97,
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

    // BST Pets 1-50
    CuSith = 4868,
    Squirrel = 27,
    Lamb = 291,
    pugil = 861,
    Opo_Opo = 32,
    Dodo = 174,
    Coblyn = 178,
    Diremite = 23,
    Megalocrab = 149,
    Wespe = 657,
    Vulture = 41,
    Mandragora = 1046,
    Geshunpest = 4859,
    Puk = 131,
    Crab = 4860,
    Mantis = 376,
    Slime = 293,
    Dullahan = 770,
    Bat = 99,
    Flying_Trap = 49,
    Ziz = 155,
    Sabotender = 143,
    Golem = 82,
    Apkallu = 4861,
    Adamantoise = 95,
    Buffalo = 140,
    Uragnite = 1053,
    Worm = 242,
    Spriggan = 4866,
    Goobbue = 4864,
    Gigantoad = 128,
    Colibri = 1155,
    Coeurl = 67,
    Raptor = 97,
    Drake = 181,
    Treant = 106,
    Antling = 194,
    Chimera = 968,
    Morbol = 147,
    Ghost = 265,
    Salamander = 152,
    Cobra = 1905,
    Hydra = 244,
    Damselfly = 1851,
    Rotting_Goobbue = 4865,
    Zu = 1421,
    Ice_Golem = 4888,
    Karlabos = 1057,
    Rafflesia = 1034,
    Behemoth = 1752,
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
