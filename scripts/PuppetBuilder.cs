// res://scripts/PuppetBuilder.cs
using Godot;
using System;
using System.Collections.Generic;


[Tool]
public partial class PuppetBuilder : Node3D
{
	[Export(PropertyHint.File, "*.glb,*.gltf")] public string GlbPath = "/mnt/data/marionette.glb";
	[Export] public bool AutoBuildOnReady = true;
	[Export] public bool DebugDrawAabbs = false;
    [ExportToolButton("Build puppet")] public Callable BuildButton => Callable.From(BuildFromGlb);

	// Naming helpers (adjust if your GLB uses different names)
	private static bool IsUpper(string name) => name.ToLower().Contains("upper");
	private static bool IsLower(string name) => name.ToLower().Contains("lower");
	private static bool IsLeg(string name) => name.ToLower().Contains("leg");
	private static bool IsArm(string name) => name.ToLower().Contains("arm");
	private static bool IsTorso(string name) => name.ToLower().Contains("torso");
	private static bool IsHead(string name) => name.ToLower().Contains("head");

	public override void _Ready()
	{
		if (Engine.IsEditorHint())
			return;

		if (AutoBuildOnReady && !string.IsNullOrEmpty(GlbPath))
		{
			// Build inside editor so you can inspect result
			BuildFromGlb();
		}
	}

    private void AddChildAndSetOwner(Node3D child, Node3D parent = null)
    {
        if (parent == null)
        {
            AddChild(child);
        } else
        {
            parent.AddChild(child);
        }
        child.Owner = GetTree().EditedSceneRoot;
    }
    
	/// <summary>
	/// Loads a GLB and builds a marionette hierarchy under this node.
	/// </summary>
	public void BuildFromGlb(string filePath)
	{
		if (!FileAccess.FileExists(filePath))
		{
			GD.PrintErr($"GLB not found: {filePath}");
			return;
		}

		var packed = ResourceLoader.Load<PackedScene>(filePath);
		if (packed == null)
		{
			GD.PrintErr($"Failed to load GLB as scene: {filePath}");
			return;
		}

		// instantiate the model scene (editor-only instance is OK)
		var model = packed.Instantiate() as Node3D;
		if (model == null)
		{
			GD.PrintErr("GLB did not instantiate to Node3D root.");
			return;
		}

		// Collect all MeshInstance3D nodes under model
		var meshes = new List<MeshInstance3D>();
		CollectMeshesRecursive(model, meshes);

		// Container for created limbs by original mesh node name
		var limbs = new Dictionary<string, MarionetteLimb>();
        GD.Print($"Num meshes: {meshes.Count}");

		// Create a RigidBody (MarionetteLimb) for each mesh, and move mesh under it
		foreach (var mesh in meshes)
		{
			// Decide a friendly name
			string logicalName = mesh.Name;

			// Create a new MarionetteLimb (must be present in project)
			var limb = new MarionetteLimb();
			limb.Name = logicalName + "_body";

			// Put limb where mesh currently is
			// limb.GlobalTransform = mesh.GlobalTransform;

			// Add limb as a child to the builder (so hierarchy is easy)
            
			AddChildAndSetOwner(limb);
            GD.Print($"Adding limb: {logicalName}");

			// Reparent mesh under limb:
			// keep world transform by making mesh top-level temporarily
            mesh.TopLevel = true;
			var oldParent = mesh.GetParent();
			if (oldParent != null && oldParent is Node parentNode)
				parentNode.RemoveChild(mesh);
            GD.Print($"Adding mesh: {mesh.Name}");
            AddChildAndSetOwner(mesh, limb);
			// Reset mesh transform so it sits in the limb's local space correctly
			mesh.Transform = Transform3D.Identity;
			mesh.TopLevel = false;

			// Build a simple capsule collision from mesh AABB
			var aabb = GetMeshAabb(mesh);
			var coll = MakeCollisionForAabb(aabb);
            GD.Print("Add collider");
            AddChildAndSetOwner(coll, limb);
			// limb.AddChild(coll);

			// basic mass tuning by approximate volume
			float approxMass = Mathf.Clamp(aabb.Size.Length() * 0.8f, 0.3f, 6f);
			limb.Mass = approxMass;

			// record limb
			limbs[mesh.Name] = limb;

			if (DebugDrawAabbs)
			{
				DebugDrawAABB(mesh, aabb);
			}
		}

		// Now create joints between upper/lower pairs (arms & legs)
		foreach (var kv in limbs)
		{
			string meshName = kv.Key;
			var limb = kv.Value;

			// Example expected names: "Leg_Upper_L", "Leg_Lower_L"
			// We try to match by "Leg" or "Arm" and "Upper"/"Lower" and Side suffix (L/R)
			string lower = meshName.ToLower();

			// find pairs where this is an upper and there's a matching lower sibling
			if (IsUpper(meshName))
			{
				// We try to find the matching lower sibling by replacing "upper" with "lower"
				string expectedLower = meshName.Replace("Upper", "Lower", StringComparison.OrdinalIgnoreCase);
				if (!limbs.ContainsKey(expectedLower))
				{
					// try other naming variants: replace "upper" with "lower" case-insensitive
					expectedLower = meshName.Replace("_upper_", "_lower_", StringComparison.OrdinalIgnoreCase);
				}

				if (limbs.ContainsKey(expectedLower))
				{
					var parentLimb = limb;
					var childLimb = limbs[expectedLower];

					// compute world pivot: top of child / bottom of parent using mesh AABBs
					var parentMesh = GetMeshUnder(parentLimb);
					var childMesh = GetMeshUnder(childLimb);

					if (parentMesh == null || childMesh == null) continue;

					var parentTop = GetMeshTopWorld(parentMesh);
					var childTop = GetMeshTopWorld(childMesh); // we will average parent bottom and child top
					var parentBottom = GetMeshBottomWorld(parentMesh);
					var pivotWorld = (parentBottom + GetMeshTopWorld(childMesh)) * 0.5f;

					// create joint: for upper -> use ConeTwist (ball-like)
					var joint = new ConeTwistJoint3D();
					joint.Name = $"{parentLimb.Name}_to_{childLimb.Name}_ctj";
					// place joint at pivot
					joint.GlobalTransform = new Transform3D(Basis.Identity, pivotWorld);

					// set basic limits (tune later)
					joint.SwingSpan = 45f;
					// joint.SwingSpan2 = 45f;
					joint.TwistSpan = 25f;
					joint.Softness = 0.6f;
					joint.Bias = 0.3f;
					joint.Relaxation = 1.0f;

					AddChildAndSetOwner(joint); // joint can be top-level; store in builder root
					// Connect Node A/B using absolute NodePaths
					joint.NodeA = parentLimb.GetPath();
					joint.NodeB = childLimb.GetPath();

					// Optionally exclude collisions
					joint.ExcludeNodesFromCollision = true;
				}
			}
			// For lower parts we create hinge joints (knee/elbow).
			// We'll create hinge only if this is a lower part's parent detection handled above when scanning uppers.
		}

		// Second pass for hinge joints: find "Lower" nodes and pair with their corresponding "Upper" parent
		foreach (var kv in limbs)
		{
			string meshName = kv.Key;
			if (IsLower(meshName))
			{
				// expected upper sibling
				string expectedUpper = meshName.Replace("Lower", "Upper", StringComparison.OrdinalIgnoreCase);
				if (!limbs.ContainsKey(expectedUpper))
					expectedUpper = meshName.Replace("_lower_", "_upper_", StringComparison.OrdinalIgnoreCase);

				if (!limbs.ContainsKey(expectedUpper))
					continue;

				var parentLimb = limbs[expectedUpper];
				var childLimb = kv.Value;

				// pivot: at top of child / bottom of parent (same as above)
				var parentMesh = GetMeshUnder(parentLimb);
				var childMesh = GetMeshUnder(childLimb);
				if (parentMesh == null || childMesh == null) continue;

				Vector3 pivotWorld = (GetMeshBottomWorld(parentMesh) + GetMeshTopWorld(childMesh)) * 0.5f;

				// create hinge joint
				var hinge = new HingeJoint3D();
				hinge.Name = $"{parentLimb.Name}_to_{childLimb.Name}_hinge";
				hinge.GlobalTransform = new Transform3D(Basis.Identity, pivotWorld);

				// Basic hinge limits: knee-like behavior
				// Godot Hinge uses Upper/Lower degrees; we set Lower=-90, Upper=0 (bend backward)
                hinge.SetParam(HingeJoint3D.Param.LimitUpper, 0f);
                hinge.SetParam(HingeJoint3D.Param.LimitLower, -90f);
                hinge.SetParam(HingeJoint3D.Param.LimitBias, 0.3f);
                hinge.SetParam(HingeJoint3D.Param.LimitRelaxation, 1f);
                // hinge.SetParam(HingeJoint3D.Param)
				// hinge.AngularLimitEnable = true;
				

				// Set a sensible rotation so hinge axis bends forward/back.
				// If your model's axis is different, change this rotation.
				hinge.RotationDegrees = new Vector3(90f, 0f, 0f);

				AddChildAndSetOwner(hinge);
				hinge.NodeA = parentLimb.GetPath();
				hinge.NodeB = childLimb.GetPath();
				hinge.ExcludeNodesFromCollision = true;
			}
		}

		// Finally call SetupLimb style defaults on each MarionetteLimb (if you want to reuse your SetupLimb logic)
		foreach (var kv in limbs)
		{
			var limb = kv.Value;
			// compute attach offset using your existing calculator
			limb.AttachOffset = AttachOffsetCalculator.ComputeAttachOffset(limb);
			// default rest length = current attach->target distance if there's a matching string under this builder node
			// (we don't create String targets here; your PuppetManager can wire them using NodePaths or you can create them)
		}

		GD.Print("PuppetBuilder finished. Inspect generated limbs & joints under the PuppetBuilder node.");
	}

    public void BuildFromGlb()
    {
        BuildFromGlb(GlbPath);
    }
	// Collect all MeshInstance3D nodes recursively
	private void CollectMeshesRecursive(Node3D n, List<MeshInstance3D> outList)
	{
		foreach (var child in n.GetChildren())
		{
			if (child is MeshInstance3D m)
			{
				outList.Add(m);
                CollectMeshesRecursive(m, outList);
			}
			else if (child is Node3D nd)
			{
				CollectMeshesRecursive(nd, outList);
			}
		}
	}

	// Returns the first MeshInstance3D direct child under a limb node (we moved the mesh under the limb earlier)
	private MeshInstance3D GetMeshUnder(MarionetteLimb limb)
	{
		foreach (var c in limb.GetChildren())
			if (c is MeshInstance3D m) return m;
		return null;
	}

	private Aabb GetMeshAabb(MeshInstance3D m)
	{
		if (m.Mesh == null) return new Aabb(Vector3.Zero, Vector3.Zero);
		var a = m.Mesh.GetAabb();
		// The mesh AABB is in mesh-local coordinates; we return it so builder uses it to construct collision shape
		return a;
	}

	private CollisionShape3D MakeCollisionForAabb(Aabb aabb)
	{
		// approximate shape using capsule oriented on Y
		var cs = new CollisionShape3D();
		var cap = new CapsuleShape3D();

		float radius = Math.Max(aabb.Size.X, aabb.Size.Z) * 0.5f;
		float height = Math.Max(0.02f, aabb.Size.Y - 2f * radius);
		cap.Radius = radius;
		cap.Height = Math.Max(0.001f, height);

		cs.Shape = cap;

		// collision transform: put center at aabb center
		var centerLocal = aabb.Position + aabb.Size * 0.5f;
		cs.Transform = new Transform3D(Basis.Identity, centerLocal);

		return cs;
	}

	private Vector3 GetMeshTopWorld(MeshInstance3D m)
	{
		var aabb = GetMeshAabb(m);
		// top local point
		var topLocal = new Vector3(aabb.Position.X + aabb.Size.X * 0.5f, aabb.Position.Y + aabb.Size.Y, aabb.Position.Z + aabb.Size.Z * 0.5f);
		return m.GlobalTransform * topLocal;
	}
	private Vector3 GetMeshBottomWorld(MeshInstance3D m)
	{
		var aabb = GetMeshAabb(m);
		var bottomLocal = new Vector3(aabb.Position.X + aabb.Size.X * 0.5f, aabb.Position.Y, aabb.Position.Z + aabb.Size.Z * 0.5f);
		return m.GlobalTransform * bottomLocal;
	}

	// Optional debug visual (editor only) to draw an AABB as a MeshInstance
	private void DebugDrawAABB(MeshInstance3D mesh, Aabb aabb)
	{
		var gem = new MeshInstance3D();
		var box = new BoxMesh();
		box.Size = aabb.Size;
		gem.Mesh = box;
		gem.GlobalTransform = new Transform3D(mesh.GlobalTransform.Basis, mesh.GlobalTransform * (aabb.Position + aabb.Size * 0.5f));
		// gem. = new Color(1, 0, 0, 0.15f);
		AddChildAndSetOwner(gem);
	}
}
