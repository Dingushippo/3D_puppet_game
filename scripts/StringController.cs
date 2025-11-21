using Godot;
using System;

[Tool]
public partial class StringController : Node3D
{
	// Called when the node enters the scene tree for the first time.
	[Export] public bool debug = true;
	[ExportGroup("Target paths")]
	[Export] public NodePath TorsoTargetPath;
	[Export] public NodePath HeadTargetPath;
	[Export] public NodePath LeftHandTargetPath;
	[Export] public NodePath RightHandTargetPath;
	[Export] public NodePath LeftFootTargetPath;
	[Export] public NodePath RightFootTargetPath;

	[ExportGroup("Move parameters")]
	[Export] public float MoveSpeed = 2.5f;
	[Export] public float SwayAmplitude = 0.2f;
	[Export] public float SwaySpeed  = 1.5f;
	[Export] public float SmoothTime = 0.06f;

	Node3D torsoTarget;
	Node3D headTarget;
	Node3D leftHandTarget;
	Node3D rightHandTarget;
	Node3D leftFootTarget;
	Node3D rightFootTarget;
	public override void _Ready()
	{
		torsoTarget = GetNode<Node3D>(TorsoTargetPath);
		headTarget = GetNode<Node3D>(HeadTargetPath);
		leftHandTarget = GetNode<Node3D>(LeftHandTargetPath);
		rightHandTarget = GetNode<Node3D>(RightHandTargetPath);
		leftFootTarget = GetNode<Node3D>(LeftFootTargetPath);
		rightFootTarget = GetNode<Node3D>(RightFootTargetPath);
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
    {
		if (!Engine.IsEditorHint())
        {
            Vector2 dirXY = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
			Vector3 dir = new Vector3(dirXY.X, 0, dirXY.Y);
			if (Input.IsActionPressed("move_up")) dir.Y += 1;
			if (Input.IsActionPressed("move_down")) dir.Y += -1;

			
			Vector3 move = dir != Vector3.Zero ? new Vector3(dir.X, dir.Y, dir.Z) * MoveSpeed * (float)delta: Vector3.Zero ;
			GlobalTranslate(move);
        }
        

		if (!debug) return;
		DebugDraw3D.DrawSphere(torsoTarget.GlobalPosition, 0.1f, Colors.Green);
		DebugDraw3D.DrawSphere(headTarget.GlobalPosition, 0.1f, Colors.Green);
		DebugDraw3D.DrawSphere(leftHandTarget.GlobalPosition, 0.1f, Colors.Green);
		DebugDraw3D.DrawSphere(rightHandTarget.GlobalPosition, 0.1f, Colors.Green);
		DebugDraw3D.DrawSphere(rightFootTarget.GlobalPosition, 0.1f, Colors.Green);
		DebugDraw3D.DrawSphere(leftFootTarget.GlobalPosition, 0.1f, Colors.Green);
    }
}
