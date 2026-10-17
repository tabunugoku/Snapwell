namespace Snapwell.Core.Physics;

/// <summary>吸着中にサイズを変えるタイミング。</summary>
public enum SizeMode
{
    /// <summary>静止を確定したときにだけサイズを変える。</summary>
    OnLanding,

    /// <summary>位置と同じばねでサイズも変える。</summary>
    Smooth,
}
