namespace PartEquipment
{
    class ModuleEquipmentItem : PartModule
    {
        [KSPField(isPersistant = true)]
        public double volume = 0;

        public double Volume
        {
            get
            {
                if (volume == 0)
                {
                    volume = PartUtils.CalculatePartVolume(part);
                    Core.Log("Autocalculated volume of " + part + ": " + volume + " l.");
                }
                return volume;
            }
        }

        public override string GetInfo() => "External volume: " + volume.ToString("N0") + " l";
    }
}
