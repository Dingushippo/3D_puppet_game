using Godot;
using System;

/// <summary>
/// Drag RigidBody3D objects by applying a PD spring force at the hit point.
/// Attach this script to a Camera3D or another always-active Node3D.
/// </summary>
public partial class DragController3D : Node3D
{
	private Camera3D _cam;
	private RigidBody3D _dragged;
	private Vector3 _localHit;      // hit point relative to body's origin (body local space)
	private float _dragDistance = 6f;

	// tuning
	[Export] public float Kp = 200f;          // stiffness
	[Export] public float Kd = 30f;           // damping
	[Export] public float MaxForce = 2000f;   // clamp force
	[Export] public float GravityCompensation = 9.8f; // upward mg compensation if you want
	[Export] public float RayMaxDistance = 80f;

	private PhysicsDirectSpaceState3D space;

	public override void _Ready()
	{
		_cam = GetViewport().GetCamera3D();
		if (_cam == null)
			GD.PrintErr("DragController3D: No Camera3D found in viewport.");
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (e is InputEventMouseButton mb)
		{
			if (mb.ButtonIndex == MouseButton.Left && mb.Pressed)
				TryStartDrag();
			if (mb.ButtonIndex == MouseButton.Left && !mb.Pressed)
				EndDrag();
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		space = GetWorld3D().DirectSpaceState;
		if (_dragged == null) return;

		UpdateDrag((float)delta);
	}

	// ----------------------
	// START / STOP
	// ----------------------
	private void TryStartDrag()
	{
		if (_cam == null) return;

		Vector2 mouse = GetViewport().GetMousePosition();
		Vector3 from = _cam.ProjectRayOrigin(mouse);
		Vector3 dir = _cam.ProjectRayNormal(mouse);

		
		var q = PhysicsRayQueryParameters3D.Create(from, from + dir * RayMaxDistance);
		q.CollideWithBodies = true;
		q.CollideWithAreas = false;

		var res = space.IntersectRay(q);
		if (res.Count == 0) return;

		// Correct Variant -> RigidBody3D cast
		var body = res["collider"].As<RigidBody3D>();
		if (body == null) return;

		_dragged = body;
		var hit = res["position"].As<Vector3>();
		_localHit = _dragged.ToLocal(hit);

		// choose default drag distance based on hit distance
		_dragDistance = (hit - _cam.GlobalPosition).Length();

		// wake the body
		_dragged.Sleeping = false;
	}

	private void EndDrag()
	{
		_dragged = null;
	}

	// ----------------------
	// DRAG UPDATE (physics)
	// ----------------------
	private void UpdateDrag(float dt)
	{
		// compute world target from mouse
		Vector2 mouse = GetViewport().GetMousePosition();
		Vector3 from = _cam.ProjectRayOrigin(mouse);
		Vector3 dir = _cam.ProjectRayNormal(mouse);

		Vector3 targetWorld = from + dir * _dragDistance;

		// current attach world pos
		Vector3 attachWorld = _dragged.ToGlobal(_localHit);

		// approximate attach point velocity: linear + angular cross r
		Vector3 pointVel = _dragged.LinearVelocity + _dragged.AngularVelocity.Cross(attachWorld - _dragged.GlobalTransform.Origin);

		// PD spring
		Vector3 error = targetWorld - attachWorld;
		Vector3 force = error * Kp - pointVel * Kd;

		// gravity compensation (approx)
		force += Vector3.Up * GravityCompensation * _dragged.Mass;

		// clamp
		if (force.Length() > MaxForce)
			force = force.Normalized() * MaxForce;

		// local offset relative to center of mass (body local)
		Vector3 localOffset = _dragged.GlobalTransform.AffineInverse() * attachWorld;

		// apply force at point (world-space force, body-local offset)
		_dragged.ApplyForce(force, localOffset);

		// debug drawing
		DebugDraw3D.DrawSphere(attachWorld, 0.04f, Colors.Orange);
		DebugDraw3D.DrawLine(attachWorld, targetWorld, Colors.Cyan);
	}
}
