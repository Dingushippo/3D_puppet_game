// PuppetBuilder.cs
using Godot;
using System;
using System.Collections.Generic;

[Tool]
public partial class PuppetManager : Node3D
{
	private const float MATERIAL_DENSIYT = 700f; // kg/m3 for wood-like material
	private const float CORRECTION = 0.4f;
	private const float MINMASS = 0.2f;
	private const float MAXMASS = 15.0f;

	[Export(PropertyHint.File, "*.glb,*.gltf")]
	public string GlbPath;

	[Export] public NodePath StringControllerPath;
	[Export] public bool AutoBuildOnReady = false;
	[Export] public float JointOffset = 0.02f;
	[ExportToolButton("Build puppet")] public Callable BuildButton => Callable.From(Build);
	[ExportToolButton("Clear puppet")] public Callable ClearButton => Callable.From(Clear);
	[Export] public bool DebugFreezeHead = false;

	private enum Side { L, R, T, B };
	private StringController stringController;

	// -----------------------------------------------------
	// SAFE EDITOR ENTRY
	// -----------------------------------------------------
	public override void _Ready()
	{
		stringController = GetNode<StringController>(StringControllerPath);

		if (AutoBuildOnReady && !string.IsNullOrEmpty(GlbPath))
		{
			GD.Print("Building");
			Clear();
			Build();
		}
	}

	public override void _Process(double delta)
	{
		// Debug stuff here if needed
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
			var originalGlobal = mesh.GlobalTransform;
			var baseName = mesh.Mesh.ResourceName.Replace("marionette_", "");

			var limb = new MarionetteLimb();
			limb.Name = baseName;
			AddChild(limb);
			limb.GlobalTransform = originalGlobal;
			limb.Owner = GetTree().EditedSceneRoot;

			// Reparent mesh into limb, preserving transform
			mesh.Name = baseName + "_mesh";
			mesh.Reparent(limb, false);
			mesh.Transform = Transform3D.Identity;   // reset inside the limb
			mesh.Owner = GetTree().EditedSceneRoot;

			// generate collider
			var aabb = mesh.Mesh.GetAabb();
			var coll = MakeCapsuleFromAabb(aabb);
			coll.Name = baseName + "_collider";
			limb.AddChild(coll);
			coll.Owner = GetTree().EditedSceneRoot;

			// mass based on volume estimate
			float size = aabb.Size.X * aabb.Size.Y * aabb.Size.Z;
			float mass = size * MATERIAL_DENSIYT * CORRECTION;
			mass = Mathf.Clamp(mass, MINMASS, MAXMASS);
			limb.Mass = mass;
			GD.Print($"Limb: {baseName}, Size: {size:F4}, Mass: {mass:F4} kg");

			limbs[baseName] = limb;
		}
		glbRoot.QueueFree();

		// -----------------------------------------------------
		// 2. Create joints
		// -----------------------------------------------------
		foreach (var limb in limbs.Values)
		{
			string name = limb.Name;
			GD.Print("Creating joint for limb: " + name);

			if (name.StartsWith("Head"))
			{
				BuildNeckJoint(limb, limbs["Torso"]);
				
				if (DebugFreezeHead)
				{
					limb.Freeze = true;
					limb.FreezeMode = RigidBody3D.FreezeModeEnum.Static;
					GD.Print("Head frozen for testing");
				}
			}
			else if (name.StartsWith("Arm_Upper"))
				BuildShoulderJoint(limb, limbs["Torso"]);

			else if (name.StartsWith("Arm_Lower"))
				BuildElbowJoint(limb, limbs[name.Replace("Lower", "Upper")]);

			else if (name.StartsWith("Leg_Upper"))
				BuildHipJoint(limb, limbs["Torso"]);

			else if (name.StartsWith("Leg_Lower"))
				BuildKneeJoint(limb, limbs[name.Replace("Lower", "Upper")]);

			// Set up string where applicable
			var stringNode = stringController.GetNodeByLimbName(name);
			GD.Print($"Limb: {name}, string node: {stringNode}");
			if (stringNode != null)
				SetupString(limb, stringNode);
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

		Vector3 size = aabb.Size;

		// Determine longest axis
		float maxDim = Mathf.Max(size.X, Mathf.Max(size.Y, size.Z));

		// Capsule axis = longest axis
		Vector3 axis;
		if (maxDim == size.X)
			axis = Vector3.Right;   // X axis
		else if (maxDim == size.Y)
			axis = Vector3.Up;      // Y axis (default)
		else
			axis = Vector3.Forward; // Z axis

		// --- Compute radius and height ---
		// radius = half of the smallest of the other two dimensions
		float radius = Mathf.Min(
			(size.Y == maxDim ? size.X : size.Y),
			(size.Z == maxDim ? size.X : size.Z)
		) * 0.5f;

		// height = length of the main axis minus two hemispheres
		float height = Mathf.Max(0.001f, maxDim - 1f * radius);

		cap.Radius = radius;
		cap.Height = height;

		// --- Set shape ---
		cs.Shape = cap;

		// --- Build rotation basis ---
		Basis basis = Basis.Identity;

		if (axis == Vector3.Right)
			basis = new Basis(new Vector3(0, 0, 1), Mathf.Pi / 2f);  // rotate Z->Y
		else if (axis == Vector3.Forward)
			basis = new Basis(new Vector3(1, 0, 0), Mathf.Pi / 2f);  // rotate X->Y

		// --- Center in local space ---
		Vector3 center = aabb.Position + size * 0.5f;

		cs.Transform = new Transform3D(basis, center);

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

		var p = GetCorrectJointPosition(head, Side.B);
		joint.GlobalPosition = p;

		joint.NodeA = torso.GetPath();
		joint.NodeB = head.GetPath();
	
		joint.SetParam(ConeTwistJoint3D.Param.SwingSpan, Mathf.DegToRad(30));
		joint.SetParam(ConeTwistJoint3D.Param.TwistSpan, Mathf.DegToRad(30));
		joint.SetParam(ConeTwistJoint3D.Param.Softness, 1f);
		joint.SetParam(ConeTwistJoint3D.Param.Relaxation, 3f);
	}

	private void BuildShoulderJoint(MarionetteLimb armUpper, MarionetteLimb torso)
	{
		var joint = new ConeTwistJoint3D();
		AddChild(joint);
		joint.Name = armUpper.Name.ToString().Replace("Arm_Upper", "Shoulder_Joint"); // Joint name = Arm_Upper_L
		joint.Owner = GetTree().EditedSceneRoot;

		var sideString = joint.Name.ToString().Split("_")[2];
		var side = Enum.Parse<Side>(sideString);
		var p = GetCorrectJointPosition(armUpper, side);
		joint.GlobalPosition = p;

		joint.NodeA = torso.GetPath();
		joint.NodeB = armUpper.GetPath();

		joint.SetParam(ConeTwistJoint3D.Param.SwingSpan, Mathf.DegToRad(90));
		joint.SetParam(ConeTwistJoint3D.Param.TwistSpan, Mathf.DegToRad(45));
		joint.SetParam(ConeTwistJoint3D.Param.Softness, 1f);
		joint.SetParam(ConeTwistJoint3D.Param.Relaxation, 8f);
	}

	private void BuildElbowJoint(MarionetteLimb lower, MarionetteLimb upper)
	{
		var joint = new HingeJoint3D();
		AddChild(joint);
		joint.Name = lower.Name.ToString().Replace("Arm_Lower", "Elbow_Joint");
		joint.Owner = GetTree().EditedSceneRoot;

		var sideString = joint.Name.ToString().Split("_")[2];
		var side = Enum.Parse<Side>(sideString);
		var p = GetCorrectJointPosition(lower, side);
		joint.GlobalPosition = p;
		joint.RotationDegrees = new Vector3(90, 0, 0);

		joint.NodeA = upper.GetPath();
		joint.NodeB = lower.GetPath();

		joint.SetParam(HingeJoint3D.Param.LimitLower, Mathf.DegToRad(-105));
		joint.SetParam(HingeJoint3D.Param.LimitUpper, 0);
		joint.SetParam(HingeJoint3D.Param.LimitRelaxation, 3f);
		// joint.SetParam(HingeJoint3D.Param., 3f);
	}

	private void BuildHipJoint(MarionetteLimb legUpper, MarionetteLimb torso)
	{
		var joint = new ConeTwistJoint3D();
		AddChild(joint);
		joint.Name = legUpper.Name.ToString().Replace("Leg_Lower", "Hip_Joint");
		joint.Owner = GetTree().EditedSceneRoot;

		var p = GetCorrectJointPosition(legUpper, Side.T);
		joint.GlobalPosition = p;

		joint.NodeA = torso.GetPath();
		joint.NodeB = legUpper.GetPath();

		joint.SetParam(ConeTwistJoint3D.Param.SwingSpan, Mathf.DegToRad(90));
		joint.SetParam(ConeTwistJoint3D.Param.TwistSpan, Mathf.DegToRad(15));
		joint.SetParam(ConeTwistJoint3D.Param.Softness, 1f);
		joint.SetParam(ConeTwistJoint3D.Param.Relaxation, 3f);
	}

	private void BuildKneeJoint(MarionetteLimb lower, MarionetteLimb upper)
	{
		var joint = new HingeJoint3D();
		AddChild(joint);
		joint.Owner = GetTree().EditedSceneRoot;
		joint.Name = lower.Name.ToString().Replace("Leg_Lower", "Knee_Joint");

		var p = GetCorrectJointPosition(lower, Side.T);
		joint.GlobalPosition = p;

		joint.NodeA = upper.GetPath();
		joint.NodeB = lower.GetPath();

		joint.SetParam(HingeJoint3D.Param.LimitLower, Mathf.DegToRad(-105));
		joint.SetParam(HingeJoint3D.Param.LimitUpper, 0);
		joint.SetParam(HingeJoint3D.Param.LimitRelaxation, 3f);
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
		return mesh.GlobalTransform * new_pos;
	}

	// -----------------------------------------------------
	// MESH EXTRACTION
	// -----------------------------------------------------
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
	// STRING SETUP
	// -----------------------------------------------------
	private void SetupString(MarionetteLimb limb, Node3D stringNode)
    {
        limb.AttachOffset = AttachOffsetCalculator.ComputeAttachOffset(limb);

		Vector3 attachWorld = limb.GlobalTransform * limb.AttachOffset;
		float initialDist = (stringNode.GlobalTransform.Origin - attachWorld).Length();

		limb.RestLength = Math.Max(0.25f, initialDist * 0.95f);
		GD.Print($"{limb.Name} RestLength: {limb.RestLength}, mass: {limb.Mass}");

		float mass = limb.Mass; // Godot RigidBody3D exposes Mass
		// conservative defaults: lower stiffness so solver is comfortable
		limb.Stiffness = 80f * Math.Max(1f, mass);   // ~40 per kg
		limb.Damping = 12f * Math.Max(1f, mass);      // damping relative to mass

		// safer max force scaled by mass
		limb.MaxForce = 50f * Math.Max(1f, mass);
		limb.Target = stringNode;
    }

	// -----------------------------------------------------
	// CLEAN
	// -----------------------------------------------------
	public void Clear()
	{
		foreach (Node child in GetChildren())
			child.QueueFree();
	}

	// -----------------------------------------------------
	// Debug
	// -----------------------------------------------------
	private void DebugJointPositions()
	{
		foreach (var child in GetChildren())
		{
			if (child is Joint3D joint)
			{
				DebugDraw3D.DrawSphere(joint.GlobalPosition, 0.02f, Colors.Red, 5.0f);
			}
		}
	}
}
