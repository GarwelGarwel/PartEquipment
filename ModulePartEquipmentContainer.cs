using System.Collections.Generic;
using System.Linq;
using UrgentContracts;

namespace PartEquipment
{
    public class ModulePartEquipmentContainer : PartModule, IPartCostModifier, IPartMassModifier
    {
        [KSPField(guiName = "Equipped parts", guiActiveEditor = true)]
        int equippedParts = 0;

        List<Part> Contents { get; set; } = new List<Part>();

        [KSPEvent(guiActiveEditor = true, name = "AddEquipment", guiName = "Add Equipment")]
        public void AddEquipment()
        {
            Core.Log("AddEquipment");
            EquipPart(PartLoader.getPartInfoByName("batteryPack"));
            equippedParts++;
        }

        /// <summary>
        /// Removes last added item
        /// </summary>
        [KSPEvent(guiActiveEditor = true, name = "RemoveEquipment", guiName = "Remove Equipment")]
        public void RemoveEquipment()
        {
            Core.Log("RemoveEquipment");
            if (Contents.Count > 0)
            {
                UnequipPart(Contents.Last());
                equippedParts--;
            }
        }

        [KSPEvent(guiActiveEditor = true, name = "ShowEquipment", guiName = "Show Equipment")]
        public void ShowEquipment()
        {
            string msg = Contents.Count + " parts:";
            foreach (Part p in Contents)
                msg += "\n" + p.partInfo.title + " (id " + p.persistentId + ")";
            ScreenMessages.PostScreenMessage(msg, 5);
        }

        public override void OnStart(StartState state)
        {
            Core.Log("OnStart for part id " + part.persistentId);
            foreach (Part p in Contents)
                p.ModulesOnStart();
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            foreach (ConfigNode n in node.GetNodes("PART"))
            {
                Core.Log("Loading part from config:\n" + n);
                Part p = PartLoader.getPartInfoByName(n.GetValue("part"))?.partPrefab;
                p.OnLoad(n);
                Core.Log("Loaded part: " + p?.name + " (" + p?.partInfo?.description + ")");
                Contents.Add(p);
                equippedParts++;
            }
            Core.Log(Contents.Count + " parts loaded.");
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            Core.Log("Saving " + Contents.Count + " parts.");

            // Saving contents
            foreach (Part p in Contents)
            {
                ConfigNode partNode = new ConfigNode("PART");
                partNode.AddValue("part", p.name);

                // Saving modules (BROKEN)
                foreach (PartModule pm in p.Modules)
                {
                    ConfigNode moduleNode = new ConfigNode("MODULE");
                    pm.OnSave(moduleNode);
                    partNode.AddNode(moduleNode);
                }

                // Saving resources
                foreach (PartResource pr in p.Resources)
                {
                    ConfigNode resourceNode = new ConfigNode("RESOURCE");
                    pr.Save(resourceNode);
                    partNode.AddNode(resourceNode);
                }
                Core.Log("Saving config:\n" + partNode);
                node.AddNode(partNode);
            }
        }

        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit) => Contents.Sum(p => p.partInfo.cost);

        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.CONSTANTLY;

        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) => Contents.Sum(p => p.mass);

        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.CONSTANTLY;

        void EquipPart(AvailablePart availablePart)
        {
            Part p = availablePart.partPrefab;
            p.OnLoad();
            p.ModulesOnStart();
            Contents.Add(p);
            foreach (PartResource pr in p.Resources)
            {
                Core.Log("Adding " + pr.amount + "/" + pr.maxAmount + " of " + pr.resourceName);
                if (part.Resources.Contains(pr.resourceName))
                {
                    part.Resources[pr.resourceName].amount += pr.amount;
                    part.Resources[pr.resourceName].maxAmount += pr.maxAmount;
                }
                else part.Resources.Add(pr);
            }
        }

        void UnequipPart(Part p)
        {
            foreach (PartResource pr in p.Resources)
            {
                Core.Log("Removing " + pr.amount + "/" + pr.maxAmount + " of " + pr.resourceName);
                part.Resources[pr.resourceName].amount -= pr.amount * (part.Resources[pr.resourceName].amount / part.Resources[pr.resourceName].maxAmount);
                part.Resources[pr.resourceName].maxAmount -= pr.maxAmount;
                if (part.Resources[pr.resourceName].maxAmount <= 0)
                    part.Resources.Remove(pr.resourceName);
            }
            Contents.Remove(p);
        }
    }
}
