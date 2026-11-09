using Raylib_cs;

namespace LodeRunner.Game.Rendering;

/// <summary>A camera behaviour. MVP ships only <see cref="ClassicCamera"/>.</summary>
public interface ICameraMode
{
    Camera3D Camera { get; }
}
