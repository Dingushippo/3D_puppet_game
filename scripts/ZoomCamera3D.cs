using Godot;

public partial class ZoomCamera3D : Camera3D
{
	[Export] public float ZoomSpeed = 1.0f;
	[Export] public float MinDistance = 2.0f;
	[Export] public float MaxDistance = 20.0f;

	private Node3D _parent;
	private float _distance;
	private Vector3 _offsetDir;

	public override void _Ready()
	{
		_parent = GetParent<Node3D>();

		// Initial offset direction (global space)
		_offsetDir = (GlobalPosition - _parent.GlobalPosition).Normalized();

		// Initial distance from parent
		_distance = GlobalPosition.DistanceTo(_parent.GlobalPosition);
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb && mb.Pressed)
		{
			if (mb.ButtonIndex == MouseButton.WheelUp)
				_distance -= ZoomSpeed;

			if (mb.ButtonIndex == MouseButton.WheelDown)
				_distance += ZoomSpeed;

			_distance = Mathf.Clamp(_distance, MinDistance, MaxDistance);

			// New global position = parent's position + direction * distance
			GlobalPosition = _parent.GlobalPosition + _offsetDir * _distance;
		}
	}
}
