// Kerbal Inventory System
// Module author: igor.zavoychinskiy@gmail.com
// License: Public Domain

// This is a combination of utilities from KSPDevUtils and KIS, used to calculate parts' volume and other properties

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PartEquipment
{
    /// <summary>Various methods to deal with the parts.</summary>
    public class PartUtils
    {
        public static PartVariant GetCurrentPartVariant(AvailablePart avPart, ConfigNode partNode)
        {
            ConfigNode variantsModule = GetModuleNode<ModulePartVariants>(partNode);
            if (variantsModule == null)
                return null;
            string selectedVariantName = variantsModule.GetValue("selectedVariant");
            if (selectedVariantName == null)
            {
                // Use the very first variant, if any.
                return avPart.partPrefab.variants.variantList.Count > 0
                    ? avPart.partPrefab.variants.variantList[0]
                    : null;
            }
            return avPart.partPrefab.variants.variantList
                .FirstOrDefault(v => v.Name == selectedVariantName);
        }

        /// <summary>Gets the part variant.</summary>
        /// <param name="part">The part to get variant for.</param>
        /// <returns>The part's variant.</returns>
        public static PartVariant GetCurrentPartVariant(Part part)
            => part?.Modules?.OfType<ModulePartVariants>().FirstOrDefault()?.SelectedVariant;

        /// <summary>Executes an action on a part with an arbitrary variant applied.</summary>
        /// <remarks>
        /// If the part doesn't support variants, then the action is executed for the unchanged prefab.
        /// </remarks>
        /// <param name="avPart">The part proto.</param>
        /// <param name="variant">
        /// The variant to apply. Set it to <c>null</c> to use the default part variant.
        /// </param>
        /// <param name="fn">
        /// The action to call once the variant is applied. The argument is a prefab part with the variant
        /// applied, so changing it or obtaining any hard references won't be a good idea. The prefab
        /// part's variant will be reverted before the method return.
        /// </param>
        public static void ExecuteAtPartVariant(AvailablePart avPart, PartVariant variant, Action<Part> fn)
        {
            if (avPart?.partPrefab == null)
            {
                Core.Log(avPart?.name + " has null partPrefab.", LogLevel.Error);
                return;
            }
            PartVariant oldPartVariant = GetCurrentPartVariant(avPart.partPrefab);
            if (oldPartVariant != null)
            {
                variant = variant ?? avPart.partPrefab.baseVariant;
                avPart.partPrefab.variants.SetVariant(variant.Name);  // Set.
                ApplyVariantOnAttachNodes(avPart.partPrefab, variant);
                fn(avPart.partPrefab);  // Run on the updated part.
                avPart.partPrefab.variants.SetVariant(oldPartVariant.Name);  // Restore.
                ApplyVariantOnAttachNodes(avPart.partPrefab, oldPartVariant);
            }
            else fn(avPart.partPrefab);
        }

        /// <summary>Creates a simplified snapshot of the part's persistent state.</summary>
        /// <remarks>
        /// This is not the same as a complete part persistent state. This state only captures the key
        /// module settings.
        /// </remarks>
        /// <param name="part">The part to snapshot. It must be a fully activated part.</param>
        /// <returns>The part's snapshot.</returns>
        public static ConfigNode PartSnapshot(Part part)
        {
            // HACK: Prefab may have fields initialized to "null". Such fields cannot be saved via
            //   BaseFieldList when making a snapshot. So, go thru the persistent fields of all prefab
            //   modules and replace nulls with a default value of the type. It's unlikely we break
            //   something since by design such fields are not assumed to be used until loaded, and it's
            //   impossible to have "null" value read from a config.
            if (ReferenceEquals(part, part.partInfo.partPrefab))
                CleanupModuleFieldsInPart(part);

            // Persist the old part's proto state to not affect it after the snapshot.
            Vessel oldVessel = part.vessel;
            ProtoPartSnapshot oldPartSnapshot = part.protoPartSnapshot;
            List<ProtoCrewMember> oldCrewSnapshot = part.protoModuleCrew;
            if (oldVessel == null)
            {
                part.vessel = part.gameObject.AddComponent<Vessel>();
                Core.Log("Making a fake vessel for the part to make a snapshot: part=" + part + ", vessel=" + part.vessel);
            }

            ProtoPartSnapshot snapshot = new ProtoPartSnapshot(part, null);
            snapshot.attachNodes = new List<AttachNodeSnapshot>();
            snapshot.srfAttachNode = new AttachNodeSnapshot("attach,-1");
            snapshot.symLinks = new List<ProtoPartSnapshot>();
            snapshot.symLinkIdxs = new List<int>();
            ConfigNode partNode = new ConfigNode("PART");
            snapshot.Save(partNode);

            // Rollback the part's proto state to the original settings.
            if (oldVessel != part.vessel)
            {
                Core.Log("Destroying the fake vessel: part=" + part + ", vessel=" + part.vessel);
                UnityEngine.Object.DestroyImmediate(part.vessel);
            }
            part.vessel = oldVessel;
            part.protoPartSnapshot = oldPartSnapshot;
            part.protoModuleCrew = oldCrewSnapshot;

            // Prune unimportant data.
            partNode.RemoveValues("parent");
            partNode.RemoveValues("position");
            partNode.RemoveValues("rotation");
            partNode.RemoveValues("istg");
            partNode.RemoveValues("dstg");
            partNode.RemoveValues("sqor");
            partNode.RemoveValues("sidx");
            partNode.RemoveValues("attm");
            partNode.RemoveValues("srfN");
            partNode.RemoveValues("attN");
            partNode.RemoveValues("connected");
            partNode.RemoveValues("attached");
            partNode.RemoveValues("flag");

            partNode.RemoveNodes("ACTIONS");
            partNode.RemoveNodes("EVENTS");
            foreach (ConfigNode moduleNode in partNode.GetNodes("MODULE"))
            {
                moduleNode.RemoveNodes("ACTIONS");
                moduleNode.RemoveNodes("EVENTS");
            }

            return partNode;
        }

        /// <summary>Returns part's model transform.</summary>
        /// <param name="part">The part to get model for.</param>
        /// <returns>
        /// The part's model transform if one was found. Or the root part's transform otherwise.
        /// </returns>
        public static Transform GetPartModelTransform(Part part)
        {
            Transform modelTransform = part.FindModelTransform("model");
            // Try kerbal's model.
            if (modelTransform == null)
                modelTransform = part.FindModelTransform("model01");
            // Try asteroid's model.
            if (modelTransform == null)
                modelTransform = part.FindModelTransform("Asteroid");
            if (modelTransform == null)
            {
                Core.Log("Cannot find model on part " + part.name, LogLevel.Error);
                return part.transform;
            }
            return modelTransform;
        }

        /// <summary>Extracts a module config node from the part config.</summary>
        /// <param name="partNode">
        /// The part's config. It can be a top-level node or the <c>PART</c> node.
        /// </param>
        /// <param name="moduleName">The name of the module to extract.</param>
        /// <returns>The module node or <c>null</c> if not found.</returns>
        public static ConfigNode GetModuleNode(ConfigNode partNode, string moduleName) => GetModuleNodes(partNode, moduleName).FirstOrDefault();

        /// <summary>Extracts a module config node from the part config.</summary>
        /// <param name="partNode">
        /// The part's config. It can be a top-level node or the <c>PART</c> node.
        /// </param>
        /// <returns>The module node or <c>null</c> if not found.</returns>
        /// <typeparam name="T">The type of the module to get node for.</typeparam>
        public static ConfigNode GetModuleNode<T>(ConfigNode partNode) => GetModuleNode(partNode, typeof(T).Name);

        /// <summary>Extracts all module config nodes from the part config.</summary>
        /// <param name="partNode">
        /// The part's config. It can be a top-level node or the <c>PART</c> node.
        /// </param>
        /// <param name="moduleName">The name of the module to extract.</param>
        /// <returns>The array of found module nodes.</returns>
        public static ConfigNode[] GetModuleNodes(ConfigNode partNode, string moduleName)
        {
            if (partNode.HasNode("PART"))
                partNode = partNode.GetNode("PART");
            return partNode.GetNodes("MODULE").Where(m => m.GetValue("name") == moduleName).ToArray();
        }

        /// <summary>Applies variant settinsg to the part attach nodes.</summary>
        /// <remarks>
        /// The stock apply variant method only does it when the active scene is editor. So if there is a
        /// part in the flight scene with a variant, it needs to be updated for the proper KIS behavior.
        /// </remarks>
        /// <param name="part">The part to apply the chnages to.</param>
        /// <param name="variant">The variant to apply.</param>
        /// <param name="updatePartPosition">
        /// Tells if any connected parts at the attach nodes need to be repositioned accordingly. This may
        /// trigger collisions in the scene, so use carefully.
        /// </param>
        public static void ApplyVariantOnAttachNodes(Part part, PartVariant variant, bool updatePartPosition = false)
        {
            foreach (AttachNode partAttachNode in part.attachNodes)
                foreach (AttachNode variantAttachNode in variant.AttachNodes)
                    if (partAttachNode.id == variantAttachNode.id)
                    {
                        if (updatePartPosition)
                            ModulePartVariants.UpdatePartPosition(partAttachNode, variantAttachNode);
                        partAttachNode.originalPosition = variantAttachNode.originalPosition;
                        partAttachNode.position = variantAttachNode.position;
                        partAttachNode.size = variantAttachNode.size;
                    }
        }

        /// <summary>Collects all the models in the part or hierarchy.</summary>
        /// <remarks>
        /// The result of this method only includes meshes and renderers. Any colliders, animations or
        /// effects will be dropped.
        /// <para>
        /// Note, that this method captures the current model state fro the part, which may be affected
        /// by animations or third-party mods. That said, each call for the same part may return different
        /// results.
        /// </para>
        /// </remarks>
        /// <param name="rootPart">The part to start scanning the assembly from.</param>
        /// <param name="goThruChildren">
        /// Tells if the parts down the hierarchy need to be captured too.
        /// </param>
        /// <returns>
        /// The root game object of the new hirerarchy. This object must be explicitly disposed when not
        /// needed anymore.
        /// </returns>
        public static GameObject GetSceneAssemblyModel(Part rootPart, bool goThruChildren = true)
        {
            GameObject modelObj = new GameObject("KisAssemblyRoot");
            modelObj.SetActive(true);

            // Add a root object with scale 1.0 to account any part model adjustments.
            GameObject partModelObj = UnityEngine.Object.Instantiate(GetPartModelTransform(rootPart).gameObject, modelObj.transform, false);
            partModelObj.SetActive(true);
            // Drop stuff that is not intended to show up in flight.
            PartLoader.StripComponent<MeshRenderer>(partModelObj, "Icon_Hidden", true);
            PartLoader.StripComponent<MeshFilter>(partModelObj, "Icon_Hidden", true);
            PartLoader.StripComponent<SkinnedMeshRenderer>(partModelObj, "Icon_Hidden", true);

            // Strip anything that is not mesh related.
            List<Joint> joints = new List<Joint>();
            List<Rigidbody> rbs = new List<Rigidbody>();
            foreach (Component component in modelObj.GetComponentsInChildren(typeof(Component)))
            {
                if (component is Transform)
                    continue;  // Transforms belong to the GameObject.
                Rigidbody rb = component as Rigidbody;
                if (rb != null)
                {
                    rbs.Add(rb);
                    continue;  // It can be tied with a joint, which must be deleted first.
                }
                Joint joint = component as Joint;
                if (joint != null)
                {
                    joints.Add(joint);
                    continue;  // They must be handled before the connected RBs handled.
                }
                if (!(component is Renderer || component is MeshFilter))
                    UnityEngine.Object.DestroyImmediate(component);
            }
            // Drop joints before rigidbodies.
            foreach (Joint joint in joints)
                UnityEngine.Object.DestroyImmediate(joint);
            // Drop rigidbodies once it's safe to do so.
            foreach (Rigidbody rb in rbs)
                UnityEngine.Object.DestroyImmediate(rb);

            if (goThruChildren)
                foreach (Part childPart in rootPart.children)
                {
                    GameObject childObj = GetSceneAssemblyModel(childPart);
                    childObj.transform.parent = modelObj.transform;
                    childObj.transform.localRotation =
                        rootPart.transform.rotation.Inverse() * childPart.transform.rotation;
                    childObj.transform.localPosition =
                        rootPart.transform.InverseTransformPoint(childPart.transform.position);
                }
            return modelObj;
        }

        /// <summary>Returns part's volume basing on its geometrics.</summary>
        /// <remarks>
        /// The volume is calculated basing on the smallest boundary box that encapsulates all the meshes
        /// in the part. The deployable parts can take much more space in the deployed state.
        /// </remarks>
        /// <param name="avPart">The part proto to get the models from.</param>
        /// <param name="variant">
        /// The part's variant. If it's <c>null</c>, then the variant will be attempted to read from
        /// <paramref name="partNode"/>.
        /// </param>
        /// <param name="partNode">
        /// The part's persistent config. It will be looked up for the variant if it's not specified.
        /// </param>
        /// <returns>The volume in liters.</returns>
        public static double CalculatePartVolume(AvailablePart avPart, PartVariant variant = null, ConfigNode partNode = null)
        {
            ModuleEquipmentItem itemModule = avPart.partPrefab?.Modules.OfType<ModuleEquipmentItem>().FirstOrDefault();
            if (itemModule != null && itemModule.volume > 0)
                return itemModule.volume; // Ignore geometry
            Vector3 boundsSize = GetPartBounds(avPart, variant, partNode);
            return boundsSize.x * boundsSize.y * boundsSize.z * 1000;
        }

        /// <summary>Returns part's volume basing on its geometrics.</summary>
        /// <remarks>
        /// The volume is calculated basing on the smallest boundary box that encapsulates all the meshes
        /// in the part. The deployable parts can take much more space in the deployed state.
        /// </remarks>
        /// <param name="part">The actual part, that exists in the scene.</param>
        /// <returns>The volume in liters.</returns>
        public static double CalculatePartVolume(Part part) => CalculatePartVolume(part.partInfo, partNode: PartSnapshot(part));

        /// <summary>Returns part's boundary box basing on its geometrics.</summary>
        /// <remarks>The size is calculated from the part prefab model.</remarks>
        /// <param name="avPart">The part proto to get the models from.</param>
        /// <param name="variant">
        /// The part's variant. If it's <c>null</c>, then the variant will be attempted to read from
        /// <paramref name="partNode"/>.
        /// </param>
        /// <param name="partNode">
        /// The part's persistent config. It will be looked up for the variant if it's not specified.
        /// </param>
        /// <returns>The volume in liters.</returns>
        public static Vector3 GetPartBounds(AvailablePart avPart, PartVariant variant = null, ConfigNode partNode = null)
        {
            Bounds bounds = default;
            if (variant == null && partNode != null)
                variant = GetCurrentPartVariant(avPart, partNode);
            ExecuteAtPartVariant(avPart, variant, p =>
            {
                Transform partModel = GetSceneAssemblyModel(p).transform;
                bounds.Encapsulate(GetMeshBounds(partModel));
                UnityEngine.Object.DestroyImmediate(partModel.gameObject);
            });
            return bounds.size;
        }

        /// <summary>Traverses thru the hierarchy and gathers all the meshes from it.</summary>
        /// <param name="model">The root model to start from.</param>
        /// <param name="meshCombines">The collection to accumulate the meshes.</param>
        /// <param name="worldTransform">
        /// The optional world matrix to apply to the mesh. If not set, then the models world's matrix
        /// will be taken.
        /// </param>
        /// <param name="considerInactive">Tells if the inactive objects must be checked as well.</param>
        public static void CollectMeshesFromModel(Transform model, ICollection<CombineInstance> meshCombines, Matrix4x4? worldTransform = null, bool considerInactive = false)
        {
            // Always use world transformation from the root.
            Matrix4x4 rootWorldTransform = worldTransform ?? model.localToWorldMatrix.inverse;

            // Get all meshes from the part's model.
            MeshFilter[] meshFilters = model
                .GetComponentsInChildren<MeshFilter>()
                // Prefab models are always inactive, so ignore the check.
                .Where(mf => considerInactive || mf.gameObject.activeInHierarchy)
                .ToArray();
            Array.ForEach(meshFilters, meshFilter =>
                meshCombines.Add(new CombineInstance
                {
                    mesh = meshFilter.sharedMesh,
                    transform = rootWorldTransform * meshFilter.transform.localToWorldMatrix
                }));

            // Skinned meshes are baked on every frame before rendering.
            SkinnedMeshRenderer[] skinnedMeshRenderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (skinnedMeshRenderers.Length > 0)
                foreach (SkinnedMeshRenderer skinnedMeshRenderer in skinnedMeshRenderers)
                {
                    CombineInstance combine = new CombineInstance();
                    combine.mesh = new Mesh();
                    skinnedMeshRenderer.BakeMesh(combine.mesh);
                    // BakeMesh() gives mesh in world scale, so don't apply it twice.
                    Matrix4x4 localToWorldMatrix = Matrix4x4.TRS(skinnedMeshRenderer.transform.position, skinnedMeshRenderer.transform.rotation, Vector3.one);
                    combine.transform = rootWorldTransform * localToWorldMatrix;
                    meshCombines.Add(combine);
                }
        }

        /// <summary>Calculates part's dry mass given the config and the variant.</summary>
        /// <param name="avPart">The part's proto.</param>
        /// <param name="variant">
        /// The part's variant. If it's <c>null</c>, then the variant will be attempted to read from
        /// <paramref name="partNode"/>.
        /// </param>
        /// <param name="partNode">
        /// The part's persistent config. It will be looked up for the variant if it's not specified.
        /// </param>
        /// <returns>The dry cost of the part.</returns>
        public double GetPartDryMass(AvailablePart avPart, PartVariant variant = null, ConfigNode partNode = null)
        {
            float itemMass = avPart.partPrefab.mass;
            if (variant == null && partNode != null)
                variant = GetCurrentPartVariant(avPart, partNode);
            ExecuteAtPartVariant(avPart, variant, p => itemMass += p.GetModuleMass(p.mass));
            return itemMass;
        }

        /// <summary>Calculates part's dry cost given the config and the variant.</summary>
        /// <param name="avPart">The part's proto.</param>
        /// <param name="variant">
        /// The part's variant. If it's <c>null</c>, then the variant will be attempted to read from
        /// <paramref name="partNode"/>.
        /// </param>
        /// <param name="partNode">
        /// The part's persistent config. It will be looked up for the various cost modifiers.
        /// </param>
        /// <returns>The dry cost of the part.</returns>
        public double GetPartDryCost(AvailablePart avPart, PartVariant variant = null, ConfigNode partNode = null)
        {
            // TweakScale compatibility
            //if (partNode != null) {
            //  var tweakScale = PartNodeUtils.GetTweakScaleModule(partNode);
            //  if (tweakScale != null) {
            //    var tweakedCost = ConfigAccessor.GetValueByPath<double>(tweakScale, "DryCost");
            //    if (tweakedCost.HasValue) {
            //      // TODO(ihsoft): Get back to this code once TweakScale supports variants.
            //      return tweakedCost.Value;
            //    }
            //    Core.Log("No dry cost specified in a tweaked part " + avPart.name + ":\n" + tweakScale, LogLevel.Error);
            //  }
            //}
            float itemCost = avPart.cost;
            if (variant == null && partNode != null)
                variant = GetCurrentPartVariant(avPart, partNode);
            ExecuteAtPartVariant(avPart, variant, p => itemCost += p.GetModuleCosts(avPart.cost));
            return itemCost;
        }

        /// <summary>Walks thru all modules in the part and fixes null persistent fields.</summary>
        /// <remarks>Used to prevent NREs in methods that persist KSP fields.
        /// <para>
        /// Bad modules that cannot be fixed will be dropped which may make the part to be not behaving as
        /// expected. It's guaranteed that the <i>stock</i> modules that need fixing will be fixed
        /// successfully. So, the failures are only expected on the modules from the third-parties mods.
        /// </para></remarks>
        /// <param name="part">The part to fix.</param>
        static void CleanupModuleFieldsInPart(Part part)
        {
            List<PartModule> badModules = new List<PartModule>();
            foreach (PartModule moduleObj in part.Modules)
            {
                PartModule module = moduleObj as PartModule;
                try
                {
                    CleanupFieldsInModule(module);
                }
                catch
                {
                    badModules.Add(module);
                }
            }
            // Cleanup modules that block KIS. It's a bad thing to do but not working KIS is worse.
            foreach (PartModule moduleToDrop in badModules)
            {
                Core.Log("Module on part prefab " + part + " is setup improperly: name=" + moduleToDrop + ". Drop it!", LogLevel.Error);
                part.RemoveModule(moduleToDrop);
            }
        }

        /// <summary>Fixes null persistent fields in the module.</summary>
        /// <remarks>Used to prevent NREs in methods that persist KSP fields.</remarks>
        /// <param name="module">The module to fix.</param>
        static void CleanupFieldsInModule(PartModule module)
        {
            // HACK: Fix uninitialized fields in science lab module.
            ModuleScienceLab scienceModule = module as ModuleScienceLab;
            if (scienceModule != null)
            {
                scienceModule.ExperimentData = new List<string>();
                Core.Log("WORKAROUND. Fix null field in ModuleScienceLab module on the part prefab: " + module, LogLevel.Important);
            }

            // Ensure the module is awaken. Otherwise, any access to base fields list will result in NRE.
            // HACK: Accessing Fields property of a non-awaken module triggers NRE. If it happens then do
            // explicit awakening of the *base* module class.
            try
            {
                module.Fields.GetEnumerator();
            }
            catch
            {
                Core.Log("WORKAROUND. Module " + module + " on part prefab is not awaken. Call Awake on it", LogLevel.Important);
                module.Awake();
            }
            foreach (BaseField field in module.Fields)
            {
                BaseField baseField = field as BaseField;
                if (baseField.isPersistant && baseField.GetValue(module) == null)
                {
                    StandardOrdinaryTypesProto proto = new StandardOrdinaryTypesProto();
                    object defValue = proto.ParseFromString("", baseField.FieldInfo.FieldType);
                    Core.Log("WORKAROUND. Found null field " + baseField.name + " in module prefab " + module + ", fixing to default value of type " + baseField.FieldInfo.FieldType + ": " + defValue, LogLevel.Important);
                    baseField.SetValue(defValue, module);
                }
            }
        }

        /// <summary>Calculates bounds from the actual meshes of the model.</summary>
        /// <remarks>Note that the result depends on the model orientation.</remarks>
        /// <param name="model">The model to find the bounds for.</param>
        /// <param name="considerInactive">Tells if inactive meshes should be considered.</param>
        /// <returns></returns>
        static Bounds GetMeshBounds(Transform model, bool considerInactive = false)
        {
            List<CombineInstance> combines = new List<CombineInstance>();
            CollectMeshesFromModel(model, combines, considerInactive: considerInactive);
            Bounds bounds = default;
            foreach (CombineInstance combine in combines)
            {
                Mesh mesh = new Mesh();
                mesh.CombineMeshes(new[] { combine });
                bounds.Encapsulate(mesh.bounds);
            }
            return bounds;
        }
    }
}
