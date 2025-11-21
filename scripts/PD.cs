using Godot;
using System;
using System.IO;

public static class PD
{
    public static Vector3 Force(Vector3 current, Vector3 target, Vector3 velocity, float kp, float kd)
    {
        Vector3 p = kp * (target - current); // Pulls toward target
        Vector3 d = -kd * velocity; // Dampens based on velocity
        return p + d;
    }
}