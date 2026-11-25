/// <summary>Retail smart-ammo lock classes, not damage armor classes.</summary>
public enum Titanfall2SmartAmmoLockType { None, Small, Large, Special, Any }

public sealed partial class Titanfall2Npc
{
	[Property] public Titanfall2SmartAmmoLockType SmartAmmoLockType { get; set; } = Titanfall2SmartAmmoLockType.Small;
	internal bool AcceptsSmallSmartAmmo => !IsTitan && SmartAmmoLockType is Titanfall2SmartAmmoLockType.Small or Titanfall2SmartAmmoLockType.Any;
}
