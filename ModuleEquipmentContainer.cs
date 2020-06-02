using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PartEquipment
{
    public class ModuleEquipmentContainer : PartModule, IPartCostModifier, IPartMassModifier
    {
        [KSPField(guiName = "Equipped parts", guiActiveEditor = true)]
        int equippedParts = 0;

        [KSPField]
        public double volume = 0;

        [KSPField(guiName = "Internal volume", guiActiveEditor = true)]
        string displayVolume;

        List<Part> Contents { get; set; } = new List<Part>();

        /// <summary>
        /// Shows a dialog to select and add a new part to the container
        /// </summary>
        [KSPEvent(guiActiveEditor = true, name = "AddEquipment", guiName = "Add Equipment")]
        public void AddEquipment()
        {
            Core.Log("AddEquipment");

            DialogGUIVerticalLayout partSelector = new DialogGUIVerticalLayout();
            partSelector.AddChild(new DialogGUIContentSizer(UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained, UnityEngine.UI.ContentSizeFitter.FitMode.MinSize));
            partSelector.AddChildren(PartLoader.LoadedPartsList
                    .Where(ap => !ap.TechHidden && ap.category != PartCategories.none)
                    .OrderBy(ap => ap.category)
                    .Select(ap => new DialogGUIButton(ap.title, () => EquipPart(ap), 200, 30, true)).ToArray());
            partSelector.AddChild(new DialogGUIButton("Cancel", null, 200, 30, true));

            PopupDialog.SpawnPopupDialog(new MultiOptionDialog("PartEquipmentAddPart", "", "Select Part to Add", HighLogic.UISkin, new DialogGUIScrollList(new Vector2(200, 400), false, true, partSelector)), false, HighLogic.UISkin);
        }

        /// <summary>
        /// Removes last added part
        /// </summary>
        [KSPEvent(guiActiveEditor = true, name = "RemoveEquipment", guiName = "Remove Equipment")]
        public void RemoveEquipment()
        {
            Core.Log("RemoveEquipment");
            if (Contents.Count > 0)
                UnequipPart(Contents.Last());
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
            Core.Log("OnStart(" + state + ") for part " + part.name + ", id " + part.persistentId);
            foreach (Part p in Contents)
                p.ModulesOnStart();
            UpdateDisplayVolume();
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
            //UpdateDisplayVolume();
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

        public override string GetInfo() => "Internal volume: " + volume + " l";

        void UpdateDisplayVolume()
        {
            displayVolume = FreeVolume.ToString("N0") + " / " + volume.ToString("N0") + " l";
        }

        public double OccupiedVolume => Contents.Sum(p => p.GetPartVolume());

        public double FreeVolume => volume - OccupiedVolume;

        void EquipPart(AvailablePart availablePart)
        {
            Core.Log("EquipPart(" + availablePart.name + ")");
            Part p = availablePart.partPrefab;
            if (p.GetPartVolume() > FreeVolume)
            {
                Core.Log("Can't equip " + p.name + ": its volume is " + p.GetPartVolume() + " l when " + FreeVolume + " / " + volume + " l available.");
                ScreenMessages.PostScreenMessage("The part doesn't fit. Part volume: " + p.GetPartVolume() + " l. Available volume: " + FreeVolume + " l.", 5);
                return;
            }
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
            equippedParts++;
            UpdateDisplayVolume();
        }

        void UnequipPart(Part p)
        {
            Core.Log("UnequipPart(" + p.name + ")");
            foreach (PartResource pr in p.Resources)
            {
                PartResource partResource = part.Resources[pr.resourceName];
                double amountToRemove = partResource.amount * pr.maxAmount / partResource.maxAmount;
                Core.Log("Removing " + amountToRemove + "/" + partResource.maxAmount + " of " + pr.resourceName);
                partResource.amount -= amountToRemove;
                partResource.maxAmount -= pr.maxAmount;
                if (partResource.maxAmount <= 0)
                    part.Resources.Remove(pr.resourceName);
            }
            Contents.Remove(p);
            equippedParts--;
            UpdateDisplayVolume();
        }
    }
}
