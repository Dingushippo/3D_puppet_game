// AttachOffsetCalculator.cs
using Godot;
using System;

public static class AttachOffsetCalculator
{
    public enum AttachType
    {
        Top,
        Bottom,
        Front,
        Back,
        Left,
        Right,
        Auto // decides based on limb name
    }

    public static Vector3 ComputeAttachOffset(RigidBody3D limb, AttachType type = AttachType.Auto)
    {
        // 1. Find a MeshInstance3D inside the limb
        MeshInstance3D mesh = FindMesh(limb);
        if (mesh == null)
        {
            GD.PrintErr($"No MeshInstance3D found under {limb.Name}");
            return Vector3.Zero;
        }

        // 2. Get mesh local-space AABB
        var aabb = mesh.GetMesh().GetAabb();

        // 3. Pick attach point in *mesh local*-space
        Vector3 localAttachPoint = aabb.Position;

        switch (DecideAttachType(limb, type))
        {
            case AttachType.Top:
                localAttachPoint = new Vector3(
                    aabb.Position.X + aabb.Size.X * 0.5f,
                    aabb.Position.Y + aabb.Size.Y,
                    aabb.Position.Z + aabb.Size.Z * 0.5f
                );
                break;

            case AttachType.Bottom:
                localAttachPoint = new Vector3(
                    aabb.Position.X + aabb.Size.X * 0.5f,
                    aabb.Position.Y,
                    aabb.Position.Z + aabb.Size.Z * 0.5f
                );
                break;

            case AttachType.Front:
                localAttachPoint = new Vector3(
                    aabb.Position.X + aabb.Size.X * 0.5f,
                    aabb.Position.Y + aabb.Size.Y * 0.5f,
                    aabb.Position.Z + aabb.Size.Z
                );
                break;

            case AttachType.Back:
                localAttachPoint = new Vector3(
                    aabb.Position.X + aabb.Size.X * 0.5f,
                    aabb.Position.Y + aabb.Size.Y * 0.5f,
                    aabb.Position.Z
                );
                break;

            case AttachType.Left:
                localAttachPoint = new Vector3(
                    aabb.Position.X,
                    aabb.Position.Y + aabb.Size.Y * 0.5f,
                    aabb.Position.Z + aabb.Size.Z * 0.5f
                );
                break;

            case AttachType.Right:
                localAttachPoint = new Vector3(
                    aabb.Position.X + aabb.Size.X,
                    aabb.Position.Y + aabb.Size.Y * 0.5f,
                    aabb.Position.Z + aabb.Size.Z * 0.5f
                );
                break;
        }

        // 4. Convert mesh-local → world → limb-local
        Vector3 world = mesh.GlobalTransform * localAttachPoint;
        Vector3 localToLimb = limb.GlobalTransform.AffineInverse() * world;

        return localToLimb;
    }

    // Find the first mesh under this limb
    private static MeshInstance3D FindMesh(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is MeshInstance3D mi)
                return mi;

            var deeper = FindMesh(child);
            if (deeper != null)
                return deeper;
        }
        return null;
    }

    // Auto-decide attach type by limb name
    private static AttachType DecideAttachType(RigidBody3D limb, AttachType requested)
    {
        if (requested != AttachType.Auto)
            return requested;

        string name = limb.Name.ToString().ToLower();

        if (name.Contains("head")) return AttachType.Top;
        if (name.Contains("torso")) return AttachType.Top;
        if (name.Contains("lowerarm") || name.Contains("hand")) return AttachType.Bottom;
        if (name.Contains("lowerleg") || name.Contains("foot")) return AttachType.Bottom;

        // default: center top
        return AttachType.Top;
    }
}
