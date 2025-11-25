using Godot;
using System;
using System.IO;

[Tool]
public partial class PuppetManager : Node3D
{
	[Export] public float BaseRestLength = .5f;
	[Export(PropertyHint.File, "*.glb,")] public string ModelFile;
	[ExportGroup("Limb nodes (RigidBody3D)")]
	[Export] public NodePath TorsoPath;
	[Export] public NodePath HeadPath;
	[Export] public NodePath LowerArmLPath;
	[Export] public NodePath LowerArmRPath;
	[Export] public NodePath LowerLegLPath;
	[Export] public NodePath LowerLegRPath;

	[ExportGroup("String targets (Node3D)")]
	[Export] public NodePath StringTorsoPath;
	[Export] public NodePath StringHeadPath;
	[Export] public NodePath StringLeftHandPath;
	[Export] public NodePath StringRightHandPath;
	[Export] public NodePath StringLeftFootPath;
	[Export] public NodePath StringRightFootPath;


	private enum Side {Left, Right};
	private enum Segment {Upper, Lower};
	private enum LimbType {Arm, Leg};
	public override void _Ready()
    {
		// HandleModel(ModelFile);
		// SetupLimb(HeadPath, StringHeadPath, BaseRestLength);
        // SetupLimb(TorsoPath, StringTorsoPath, BaseRestLength + 0.8f);
        // SetupLimb(LowerArmLPath, StringLeftHandPath, BaseRestLength + 1.8f);
        // SetupLimb(LowerArmRPath, StringRightHandPath, BaseRestLength + 1.8f);
        // SetupLimb(LowerLegLPath, StringLeftFootPath, BaseRestLength + 2.8f);
        // SetupLimb(LowerLegRPath, StringRightFootPath, BaseRestLength + 2.8f);
    }


	private void SetupLimb(NodePath limbPath, NodePath stringPath, float restLength)
    {
        RigidBody3D limbNode = GetNodeOrNull<RigidBody3D>(limbPath);
		Node3D stringNode = GetNodeOrNull<Node3D>(stringPath);

		if (limbNode is null || stringNode is null) throw new Exception("Missing node or malformed path");

		if (limbNode is MarionetteLimb limb)
        {
            limb.AttachOffset = AttachOffsetCalculator.ComputeAttachOffset(limbNode);

			Vector3 attachWorld = limbNode.GlobalTransform * limb.AttachOffset;
        	float initialDist = (stringNode.GlobalTransform.Origin - attachWorld).Length();

			limb.RestLength = Math.Max(0.25f, initialDist * 0.95f);
			GD.Print($"{limb.Name} RestLength: {limb.RestLength}, mass: {limb.Mass}");

			float mass = limbNode.Mass; // Godot RigidBody3D exposes Mass
			// conservative defaults: lower stiffness so solver is comfortable
			limb.Stiffness = 80f * Math.Max(1f, mass);   // ~40 per kg
			limb.Damping = 12f * Math.Max(1f, mass);      // damping relative to mass

			// safer max force scaled by mass
			limb.MaxForce = 50f * Math.Max(1f, mass);

			limb.Target = stringNode;
        } 
		else
        {
            throw new Exception($"{limbPath} is not MarionetteLimb");
        }
    }

	private void HandleModel(string filePath)
    {
        Node modelScene = ResourceLoader.Load<PackedScene>(filePath).Instantiate();
		Node3D root = modelScene.GetChild<Node3D>(0);
		AddNodesRecursive(root);
    }

	private void AddNodesRecursive(Node3D parent)
    {
        foreach (Node3D child in parent.GetChildren())
        {
			String[] naming = child.Name.ToString().Split("_");
			switch (naming[0]) {
                case "Torso":
					child.Reparent(this);
					// AddChild(child);
					continue;
                default:
					// parent.AddChild(child);
					child.Reparent(parent);
					continue;
            }
        }
    }

	private void HandleLimb(MeshInstance3D limbMesh, LimbType type, Segment segment, Side side)
    {
        				
    }

	// private Joint3D GetCorrectJoint(Segment segment, Side side)
    // {
    //     if (segment == Segment.Upper)
    //     {
    //         Joint3D joint = new ConeTwistJoint3D();
    //     }
	// 	else if (segment == Segment.Lower)
    //     {
    //         HingeJoint3D joint = new HingeJoint3D();
	// 		joint.SetParam(HingeJoint3D.Param.LimitUpper, 0);
	// 		joint.SetParam(HingeJoint3D.Param.LimitLower, -90);
	// 		joint.SetParam(HingeJoint3D.Param.LimitBias, 0.3f);
	// 		joint.SetParam(HingeJoint3D.Param.LimitRelaxation, 1f);
	// 		joint.RotateX(90);
    //     }
    // }
}
