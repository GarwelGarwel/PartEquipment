using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PartEquipment
{
    class ModuleEquipmentItem : PartModule
    {
        [KSPField(isPersistant = true)]
        public double volume = 0;

        public override string GetInfo() => "External volume: " + volume.ToString("N0") + " l";
    }
}
