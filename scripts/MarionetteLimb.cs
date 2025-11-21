using Godot;
using System;
using System.ComponentModel;

[Tool]
public partial class MarionetteLimb : RigidBody3D
{
	[Export] public Node3D Target;
	[Export] public Vector3 AttachOffset = Vector3.Zero;
	[Export] public float RestLength = 2f; // how long the string is at rest
	[Export] public float Stiffness = 200f;  // string tension
	[Export] public float Damping = 15f;      // damp oscillations
	[Export] public float MaxForce = 500f; // For stability
	[Export] public bool debug = true;

	
	public override void _PhysicsProcess(double delta)
	{
		if (Target == null) return;

		Vector3 attachWorld = GlobalTransform * AttachOffset;
		Vector3 targetWorld = Target.GlobalTransform.Origin;

		Vector3 deltaVec = targetWorld - attachWorld;
		float dist = deltaVec.Length();

		if (float.IsNaN(dist) || dist <= 1e-4f) return; // guard

		float stretch = dist - RestLength;

		// Only pull when string is taut
		if (stretch > 0.001f)
		{
			Vector3 dir = deltaVec / dist; // normalized safely
			Vector3 velocity = LinearVelocity + AngularVelocity.Cross(attachWorld - GlobalTransform.Origin);

			// compute desired force
			Vector3 rawForce = dir * (stretch * Stiffness) - velocity * Damping;

			// clamp force magnitude
			float maxF = MaxForce > 0 ? MaxForce : 200f * Mass;
			Vector3 force = rawForce;
			if (force.Length() > maxF)
				force = force.Normalized() * maxF;

			// extra safety: scale by delta to avoid per-tick explosion if needed
			// (only if you observe step-dependent behavior)
			force *= (float)Mathf.Clamp(delta * 60f, 0.5f, 1.5f);

			// compute local offset relative to center of mass (local coordinates)
			Vector3 localOffset = GlobalTransform.AffineInverse() * attachWorld;

			// final guard for NaNs
			if (float.IsNaN(localOffset.X) || float.IsNaN(force.X)) return;

			ApplyForce(force, localOffset);
		}
	}

	public override void _Process(double delta)
	{	
		if (!debug || Target == null) return;

		// 1. World position of attach point
		Vector3 attachWorld = GlobalTransform * AttachOffset;

		// 2. Target world position
		Vector3 targetWorld = Target.GlobalTransform.Origin;

		DebugDraw3D.DrawSphere(attachWorld, 0.1f, Colors.Red);
		DebugDraw3D.DrawLine(attachWorld, Target.GlobalPosition, Colors.Cyan);
	}


}

