using Godot;
using System;

[Tool]
public partial class PuppetManager : Node3D
{
	[Export] public float BaseRestLength = .5f;
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
	public override void _Ready()
    {
		SetupLimb(HeadPath, StringHeadPath, BaseRestLength);
        SetupLimb(TorsoPath, StringTorsoPath, BaseRestLength + 0.8f);
        SetupLimb(LowerArmLPath, StringLeftHandPath, BaseRestLength + 1.8f);
        SetupLimb(LowerArmRPath, StringRightHandPath, BaseRestLength + 1.8f);
        SetupLimb(LowerLegLPath, StringLeftFootPath, BaseRestLength + 2.8f);
        SetupLimb(LowerLegRPath, StringRightFootPath, BaseRestLength + 2.8f);
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
}
