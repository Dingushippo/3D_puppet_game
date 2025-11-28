// PuppetBuilder.cs
using Godot;
using System;
using System.Collections.Generic;

[Tool]
public partial class PuppetBuilder : Node3D
{
	[Export(PropertyHint.File, "*.glb,*.gltf")]
	public string GlbPath;

	[Export] public bool AutoBuildOnReady = false;
	[Export] public float JointOffset = 0.02f;
	[ExportToolButton("Build puppet")] public Callable BuildButton => Callable.From(Build);
	[ExportToolButton("Clear puppet")] public Callable ClearButton => Callable.From(Clear);

	private enum Side {L, R, T, B};

	// -----------------------------------------------------
	// SAFE EDITOR ENTRY
	// -----------------------------------------------------
	public override void _Ready()
	{
		// if (!Engine.IsEditorHint()) return;

		GD.Print($"Build on ready: {AutoBuildOnReady}, glb path: {GlbPath}");
		if (AutoBuildOnReady && !string.IsNullOrEmpty(GlbPath))
			GD.Print("Building");
			Build();
	}

	// -----------------------------------------------------
	// MAIN BUILD
	// -----------------------------------------------------
	public void Build()
	{
		if (string.IsNullOrEmpty(GlbPath))
		{
			GD.PrintErr("No GLB path specified!");
			return;
		}

		Clear();

		PackedScene src = ResourceLoader.Load<PackedScene>(GlbPath);
		if (src == null) { GD.PrintErr("Failed to load GLB"); return; }

		Node3D glbRoot = src.Instantiate<Node3D>();
		AddChild(glbRoot);

		// Collect all mesh instances
		List<MeshInstance3D> meshes = new();
		CollectMeshes(glbRoot, meshes);

		Dictionary<string, MarionetteLimb> limbs = new();

		// -----------------------------------------------------
		// 1. Create limbs
		// -----------------------------------------------------
		foreach (var mesh in meshes)
		{
			var limb = new MarionetteLimb();
			limb.Name = mesh.Name;
			AddChild(limb);
			limb.Owner = GetTree().EditedSceneRoot;

			// Reparent mesh into limb, preserving transform
			mesh.Name = limb.Name + "_mesh";
			mesh.Reparent(limb);
			// limb.AddChild(mesh);
			// mesh.Reparent(limb);
			mesh.Owner = GetTree().EditedSceneRoot;

			// generate collider
			var aabb = GetTransformedAabb(mesh);
			var coll = MakeCapsuleFromAabb(aabb);
			coll.Name = limb.Name + "_collider";
			limb.AddChild(coll);
			coll.Owner = GetTree().EditedSceneRoot;

			limbs[limb.Name] = limb;
		}
		glbRoot.QueueFree();

		// -----------------------------------------------------
		// 2. Create joints
		// -----------------------------------------------------
		foreach (var limb in limbs.Values)
		{
			string name = limb.Name;

			if (name.StartsWith("Head"))
				BuildNeckJoint(limb, limbs["Torso"]);

			else if (name.StartsWith("Arm_Upper"))
				BuildShoulderJoint(limb, limbs["Torso"]);

			else if (name.StartsWith("Arm_Lower"))
				BuildElbowJoint(limb, limbs[name.Replace("Lower", "Upper")]);

			else if (name.StartsWith("Leg_Upper"))
				BuildHipJoint(limb, limbs["Torso"]);

			else if (name.StartsWith("Leg_Lower"))
				BuildKneeJoint(limb, limbs[name.Replace("Lower", "Upper")]);
		}

		GD.Print("Puppet build complete.");
	}

	// -----------------------------------------------------
	// COLLISION SHAPES
	// -----------------------------------------------------
	private CollisionShape3D MakeCapsuleFromAabb(Aabb aabb)
	{
		var cs = new CollisionShape3D();
		var cap = new CapsuleShape3D();

		// choose main axis = tallest dimension
		float x = aabb.Size.X;
		float y = aabb.Size.Y;
		float z = aabb.Size.Z;

		// default orientation = Y axis
		cap.Radius = Mathf.Max(x, z) * 0.5f;
		cap.Height = Mathf.Max(0.001f, y - 2f * cap.Radius);

		cs.Shape = cap;

		// center to AABB
		Vector3 center = aabb.Position + aabb.Size * 0.5f;
		cs.Transform = new Transform3D(Basis.Identity, center);

		return cs;
	}

	// -----------------------------------------------------
	// JOINT HELPERS
	// -----------------------------------------------------
	private void BuildNeckJoint(MarionetteLimb head, MarionetteLimb torso)
	{
		var joint = new ConeTwistJoint3D();
		AddChild(joint);
		joint.Name = "Neck_Joint";
		joint.Owner = GetTree().EditedSceneRoot;

		joint.NodeA = torso.GetPath();
		joint.NodeB = head.GetPath();

		var p = GetCorrectJointPosition(head, Side.B);
		joint.GlobalPosition = p;
	}

	private void BuildShoulderJoint(MarionetteLimb armUpper, MarionetteLimb torso)
	{
		var joint = new ConeTwistJoint3D();
		AddChild(joint);
		joint.Name = armUpper.Name.ToString().Replace("Arm_Upper", "Shoulder_Joint"); // Joint name = Arm_Upper_L
		joint.Owner = GetTree().EditedSceneRoot;

		joint.NodeA = torso.GetPath();
		joint.NodeB = armUpper.GetPath();

		var sideString = joint.Name.ToString().Split("_")[2];
		var side = Enum.Parse<Side>(sideString);
		var p = GetCorrectJointPosition(armUpper, side);
		joint.GlobalPosition = p;
	}

	private void BuildElbowJoint(MarionetteLimb lower, MarionetteLimb upper)
	{
		var joint = new HingeJoint3D();
		AddChild(joint);
		joint.Name = lower.Name.ToString().Replace("Arm_Lower", "Elbow_Joint");
		joint.Owner = GetTree().EditedSceneRoot;

		joint.NodeA = upper.GetPath();
		joint.NodeB = lower.GetPath();

		// joint.GlobalPosition = FindSurfacePointBetween(upper, lower);
		// joint.RotationDegrees = new Vector3(90, 0, 0);
		var sideString = joint.Name.ToString().Split("_")[2];
		var side = Enum.Parse<Side>(sideString);
		var p = GetCorrectJointPosition(lower, side);
		joint.GlobalPosition = p;

		joint.SetParam(HingeJoint3D.Param.LimitLower, -105);
		joint.SetParam(HingeJoint3D.Param.LimitUpper, 0);
	}

	private void BuildHipJoint(MarionetteLimb legUpper, MarionetteLimb torso)
	{
		var joint = new ConeTwistJoint3D();
		AddChild(joint);
		joint.Name = legUpper.Name.ToString().Replace("Leg_Lower", "Hip_Joint");
		joint.Owner = GetTree().EditedSceneRoot;

		joint.NodeA = torso.GetPath();
		joint.NodeB = legUpper.GetPath();

		var p = GetCorrectJointPosition(legUpper, Side.T);
		joint.GlobalPosition = p;
	}

	private void BuildKneeJoint(MarionetteLimb lower, MarionetteLimb upper)
	{
		var joint = new HingeJoint3D();
		AddChild(joint);
		joint.Owner = GetTree().EditedSceneRoot;
		joint.Name = lower.Name.ToString().Replace("Leg_Lower", "Knee_Joint");

		joint.NodeA = upper.GetPath();
		joint.NodeB = lower.GetPath();

		// joint.GlobalPosition = FindSurfacePointBetween(upper, lower);
		// joint.RotationDegrees = new Vector3(90, 0, 0);
		var p = GetCorrectJointPosition(lower, Side.T);
		joint.GlobalPosition = p;

		joint.SetParam(HingeJoint3D.Param.LimitLower, -105);
		joint.SetParam(HingeJoint3D.Param.LimitUpper, 0);
	}

	private Vector3 GetCorrectJointPosition(MarionetteLimb limb, Side? side)
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
		return new_pos;
	}
	
	// -----------------------------------------------------
	// AABB EXTRACTION
	// -----------------------------------------------------
	private Aabb GetTransformedAabb(MeshInstance3D mesh)
	{
		if (mesh.Mesh == null)
			return new Aabb(mesh.GlobalPosition, Vector3.One * 0.01f);

		var local = mesh.Mesh.GetAabb();
		Transform3D xf = mesh.Transform;

		Vector3[] corners = new Vector3[8];
		corners[0] = local.Position;
		corners[1] = local.Position + new Vector3(local.Size.X, 0, 0);
		corners[2] = local.Position + new Vector3(0, local.Size.Y, 0);
		corners[3] = local.Position + new Vector3(0, 0, local.Size.Z);
		corners[4] = local.Position + local.Size;
		corners[5] = local.Position + new Vector3(local.Size.X, local.Size.Y, 0);
		corners[6] = local.Position + new Vector3(local.Size.X, 0, local.Size.Z);
		corners[7] = local.Position + new Vector3(0, local.Size.Y, local.Size.Z);

		for (int i = 0; i < 8; i++)
			corners[i] = xf * corners[i];

		Aabb res = new Aabb(corners[0], Vector3.Zero);
		for (int i = 1; i < 8; i++)
			res = res.Expand(corners[i]);

		return res;
	}

	private void CollectMeshes(Node node, List<MeshInstance3D> list)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is MeshInstance3D mi)
				list.Add(mi);

			if (child is Node3D n3)
				CollectMeshes(n3, list);
		}
	}

	// -----------------------------------------------------
	// CLEAN
	// -----------------------------------------------------
	public void Clear()
	{
		foreach (Node child in GetChildren())
			child.QueueFree();
	}
}
