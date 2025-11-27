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
	[Export] public float JointOffset = 0f;
	[ExportToolButton("Build puppet")] public Callable BuildButton => Callable.From(BuildFromGlb);
	[ExportToolButton("Clear puppet")] public Callable ClearButton => Callable.From(ClearChildren);

	// Naming helpers (adjust if your GLB uses different names)

	private enum LimbType { Torso, Head, Arm, Leg };
	private enum SegmentType { Upper, Lower };
	private enum Side { L, R, B, T };

	private LimbType ParseLimbType(string name)
	{
		string prefix = name.Split("_")[0];
		return Enum.Parse<LimbType>(prefix);
	}
	private SegmentType? ParseSegmentType(string name)
	{
		string[] splitList = name.Split("_");
		if (splitList.Length < 2) return null;
		string prefix = splitList[1];
		return Enum.Parse<SegmentType>(prefix);
	}
	private Side? ParseSide(string name)
	{
		string[] splitList = name.Split("_");
		if (splitList.Length < 3) return null;
		string prefix = splitList[2];
		return Enum.Parse<Side>(prefix);
	}

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

	public override void _Process(double delta)
	{
		if (!Engine.IsEditorHint() || !DebugDrawAabbs)
			return;
		foreach (var child in GetChildren())
		{
			var mesh = child.GetChild<MeshInstance3D>(0);
			var aabb = GetMeshAabb(mesh);
			DebugDraw3D.DrawAabb(aabb);
		}

	}

	private void AddChildAndSetOwner(Node3D child, Node3D parent = null)
	{
		if (parent == null)
		{
			AddChild(child);
		}
		else if (child.GetParent() != null)
		{
			child.Reparent(parent);
		}
		else
		{
			parent.AddChild(child);
		}
		child.Owner = GetTree().EditedSceneRoot;
	}

	public void BuildFromGlb(string filePath)
	{
		if (!FileAccess.FileExists(filePath))
		{
			GD.PrintErr($"GLB not found: {filePath}");
			return;
		}

		ClearChildren();

		var packed = ResourceLoader.Load<PackedScene>(filePath);
		var model = packed.Instantiate() as Node3D;

		// Collect all MeshInstance3D nodes under model
		var meshes = new List<MeshInstance3D>();
		CollectMeshesRecursive(model, meshes);

		// Create a Limb for each mesh, and store in limbs dict
		foreach (var mesh in meshes)
		{
			var limb = new MarionetteLimb();

			// Rename stuff
			var meshName = mesh.Name;
			mesh.Name = meshName + "_mesh";
			limb.Name = meshName;

			AddChildAndSetOwner(limb);
			limb.UniqueNameInOwner = true;
			AddChildAndSetOwner(mesh, limb);

			var aabb = GetMeshAabb(mesh);
			var coll = MakeCollisionForAabb(aabb);
			coll.Name = meshName + "_collider";

			AddChildAndSetOwner(coll, limb);

			float approxMass = Mathf.Clamp(aabb.Size.Length() * 0.8f, 0.3f, 6f);
			limb.Mass = approxMass;
		}


		// Now create joints between upper/lower pairs (arms & legs)
		foreach (MarionetteLimb limb in GetChildren())
		{
			string nodeName = limb.Name;
			var type = ParseLimbType(nodeName);
			var segment = ParseSegmentType(nodeName);
			var side = ParseSide(nodeName);

			if (type == LimbType.Torso) continue;

			GD.Print($"Type: {type}, segment: {segment}, side: {side}");

			var torsoNode = GetNode<MarionetteLimb>("Torso");

			if (type == LimbType.Head)
			{
				var joint = new ConeTwistJoint3D();
				AddChildAndSetOwner(joint, torsoNode);
				MoveJointToLimb(joint, limb, Side.B);
				AddChildAndSetOwner(limb, joint);
				joint.Name = "Neck_Joint";
				joint.NodeA = torsoNode.GetPath();
				joint.NodeB = limb.GetPath();
			}
			else if (segment == SegmentType.Upper)
			{
				var joint = new ConeTwistJoint3D();
				AddChildAndSetOwner(joint, torsoNode);
				var joint_side = type == LimbType.Arm ? side : Side.T;
				MoveJointToLimb(joint, limb, joint_side);
				AddChildAndSetOwner(limb, joint);
				joint.Name = type == LimbType.Arm ? $"Shoulder_Joint_{side}" : $"Hip_Joint_{side}";
				joint.NodeA = torsoNode.GetPath();
				joint.NodeB = limb.GetPath();
			}
			else if (segment == SegmentType.Lower)
			{
				var upperName = limb.Name.ToString().Replace("Lower", "Upper");
				var upperLimb = torsoNode.GetNode<MarionetteLimb>("%" + upperName);
				var joint = new HingeJoint3D();
				AddChildAndSetOwner(joint, upperLimb);
				var joint_side = type == LimbType.Arm ? side : Side.T;
				MoveJointToLimb(joint, limb, joint_side);
				AddChildAndSetOwner(limb, joint);
				joint.Name = type == LimbType.Arm ? $"Elbow_Joint_{side}" : $"Knee_Joint_{side}";
				joint.NodeA = upperLimb.GetPath();
				joint.NodeB = limb.GetPath();
			}
			else
			{
				throw new Exception("Invalid limb");
			}
		}
		GD.Print("PuppetBuilder finished. Inspect generated limbs & joints under the PuppetBuilder node.");
	}

	public void BuildFromGlb()
	{
		BuildFromGlb(GlbPath);
	}

	private void MoveJointToLimb(Joint3D joint, MarionetteLimb limb, Side? side)
	{
		var mesh = limb.GetChild<MeshInstance3D>(0);
		var aabb = mesh.Mesh.GetAabb();
		var new_pos = aabb.Position;
		switch (side)
		{
			case Side.L:
				new_pos.X += JointOffset;
				new_pos.Z += aabb.Size.Z / 2;
				new_pos.Y += aabb.Size.Y / 2;
				break;
			case Side.R:
				new_pos.X += aabb.Size.X - JointOffset;
				new_pos.Z += aabb.Size.Z / 2;
				new_pos.Y += aabb.Size.Y / 2;
				break;
			case Side.B:
				new_pos.X += aabb.Size.X / 2;
				new_pos.Z += aabb.Size.Z / 2;
				new_pos.Y -= JointOffset;
				break;
			case Side.T:
				new_pos.X += aabb.Size.X / 2;
				new_pos.Z += aabb.Size.Z / 2;
				new_pos.Y += aabb.Size.Y - JointOffset;
				break;
		}
		joint.GlobalPosition = new_pos;
	}

	public void ClearChildren()
	{
		foreach (var child in GetChildren())
		{
			child.QueueFree();
		}
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
}
