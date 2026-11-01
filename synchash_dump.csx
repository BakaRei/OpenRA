// C# script to dump SyncHash components
// Run with: dotnet script synchash_dump.csx
// or: dotnet-script synchash_dump.csx

// Since we can't easily use the OpenRA assemblies in a script,
// let's just compute what we need manually.

// The hash values we need to check:
// bool true = 0xaaa = 2730
// bool false = 0x555 = 1365

// Shroud: disabled (bool field) = false → hash = 1365
// PlayerResources: Cash (int field) ^ Resources (int field) ^ ResourceCapacity (int field)
//   At tick 0: Cash=5000, Resources=0, ResourceCapacity=0 → hash = 5000
// ProductionQueue: Enabled (bool prop) ^ IsValidFaction (bool prop)
//   Both true → hash = 2730 ^ 2730 = 0
// MissionObjectives: ObjectivesHash (int prop) = 0 → hash = 0
// DeveloperMode: 7 bool fields, all false → hash = 1365^1365^1365^1365^1365^1365^1365 = 1365
// PowerManager: PowerProvided (int prop) ^ PowerDrained (int prop) = 0 ^ 0 = 0
// GpsWatcher: explored (bool field) + Launched (bool prop) + GrantedAllies (bool prop) + Granted (bool prop)
//   All false → hash = 1365^1365^1365^1365 = 0
// FrozenActorLayer: VisibilityHash (int field) ^ FrozenHash (int field) = 0 ^ 0 = 0
// PlayerExperience: Experience (int prop) = 0 → hash = 0

// So per player: [1365, 5000, 0, 0, 0, 0, 0, 0, 0, 1365, 0, 0, 0, 0]
// That's 14 traits, 2 with non-zero hash

Console.WriteLine("Bool true: " + 0xaaa);
Console.WriteLine("Bool false: " + 0x555);
Console.WriteLine("Shroud hash: " + 0x555);
Console.WriteLine("PlayerResources hash: " + 5000);
Console.WriteLine("PQ hash (true,true): " + (0xaaa ^ 0xaaa));
Console.WriteLine("MissionObjectives hash: 0");
Console.WriteLine("DeveloperMode hash: " + (0x555 ^ 0x555 ^ 0x555 ^ 0x555 ^ 0x555 ^ 0x555 ^ 0x555));
Console.WriteLine("PowerManager hash: 0");
Console.WriteLine("GpsWatcher hash: " + (0x555 ^ 0x555 ^ 0x555 ^ 0x555));
Console.WriteLine("FrozenActorLayer hash: 0");
Console.WriteLine("PlayerExperience hash: 0");
