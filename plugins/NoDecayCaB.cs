using Rust;

namespace Oxide.Plugins
{
	[Info("NoDecayCaB", "Own3r/rostov114", "1.0.1")]
	class NoDecayCaB : RustPlugin
	{
		private object OnEntityTakeDamage(BaseCombatEntity combatentity, HitInfo hitinfo)
		{
			if ((combatentity is MiniCopter || combatentity is MotorRowboat || combatentity is RidableHorse) && hitinfo != null)
			{
				if (hitinfo.damageTypes.Get(DamageType.Decay) > 0)
				{
					BaseEntity entity = combatentity as BaseEntity;

					if (entity != null)
					{
						BuildingPrivlidge buildingprivlidge = entity.GetBuildingPrivilege();
						if (buildingprivlidge != null)
						{
							return false;
						}
					}
				}
			}
 
			return null;
		}
	}
}